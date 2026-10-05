using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DuckNote.App.Theme;

namespace DuckNote.App.Ducks;

public sealed class DuckPond(Canvas canvas)
{
    private const double DuckWidth = 512;
    private const double DuckHeight = 512;

    private readonly List<Duck> _ducks = [];
    private readonly Random _random = new();
    private ImageBrush? _mask;
    private bool _running;
    private TimeSpan _lastFrame;

    private const double FrameMilliseconds = 40;

    private sealed class Duck
    {
        public double X, Y, Size, VX, VY, Wobble, WobbleSpeed;
        public required Shape Shape;
        public required RotateTransform Rotate;
        public required TranslateTransform Move;
    }

    public void Build(bool enabled, int count, int opacityPercent, string theme)
    {
        canvas.Children.Clear();
        _ducks.Clear();

        if (!enabled || count <= 0)
        {
            return;
        }

        _mask ??= LoadMask();

        double width = Math.Max(400.0, canvas.ActualWidth);
        double height = Math.Max(300.0, canvas.ActualHeight);
        double opacity = opacityPercent / 100.0;
        SolidColorBrush ink = ThemeTokens.Brush(ThemeTokens.Colour(theme, "DuckOrange"));

        for (int i = 0; i < count; i++)
        {
            double size = (_random.NextDouble() * 180) + 180;
            (double x, double y) = FindSpot(size, width, height);

            double angle = _random.NextDouble() * Math.PI * 2;
            double speed = 0.4 + (_random.NextDouble() * 0.7);

            // Il rettangolo nasce GRANDE QUANTO SERVE, non 512x512 poi
            // rimpicciolito: con una maschera addosso, il compositore paga la
            // dimensione dichiarata, non quella che si vede. Ventidue rettangoli
            // da mezzo megapixel ridisegnati a ogni fotogramma erano la causa
            // dello scatto, misurata accendendo e spegnendo le papere.
            RotateTransform rotate = new() { CenterX = size / 2, CenterY = size / 2 };
            TranslateTransform move = new();

            TransformGroup group = new();
            group.Children.Add(rotate);
            group.Children.Add(move);

            Shape shape = new Rectangle
            {
                Width = size,
                Height = size,
                OpacityMask = _mask,
                Fill = ink,
                Opacity = opacity,
                RenderTransform = group,
                IsHitTestVisible = false,
                CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = false }
            };

            canvas.Children.Add(shape);
            Duck duck = new()
            {
                X = x,
                Y = y,
                Size = size,
                VX = Math.Cos(angle) * speed,
                VY = Math.Sin(angle) * speed,
                Wobble = _random.NextDouble() * Math.PI * 2,
                WobbleSpeed = 0.008 + (_random.NextDouble() * 0.008),
                Shape = shape,
                Rotate = rotate,
                Move = move
            };

            Place(duck);
            _ducks.Add(duck);
        }
    }

    private (double X, double Y) FindSpot(double size, double width, double height)
    {
        double x = _random.NextDouble() * width;
        double y = _random.NextDouble() * height;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            bool clear = true;
            foreach (Duck other in _ducks)
            {
                double dx = x - other.X, dy = y - other.Y;
                if (Math.Sqrt((dx * dx) + (dy * dy)) < (size + other.Size) / 2)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
            {
                return (x, y);
            }

            x = _random.NextDouble() * width;
            y = _random.NextDouble() * height;
        }

        return (x, y);
    }

    /// <summary>
    /// Il movimento e' agganciato al disegno, non a un timer.
    /// </summary>
    /// <remarks>
    /// Un DispatcherTimer a 40 ms scatta quando gli pare, e i suoi istanti non
    /// coincidono con quelli in cui WPF compone il fotogramma: alcune posizioni
    /// vengono scritte subito dopo che il fotogramma e' partito e si vedono solo
    /// al giro dopo, altre due volte. Da qui lo scatto, anche con la CPU
    /// scarica. CompositionTarget.Rendering arriva una volta per fotogramma,
    /// appena prima che venga disegnato.
    /// </remarks>
    public void Start()
    {
        if (_ducks.Count == 0 || _running)
        {
            return;
        }

        _running = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame)
        {
            return;
        }

        // Il tempo trascorso DAVVERO fra due fotogrammi: su uno schermo a 144 Hz
        // le papere andrebbero altrimenti due volte e mezza piu' in fretta che
        // su uno a 60, perche' i fotogrammi sono piu' fitti.
        TimeSpan now = frame.RenderingTime;
        if (_lastFrame == TimeSpan.Zero)
        {
            _lastFrame = now;
            return;
        }

        double elapsed = (now - _lastFrame).TotalMilliseconds;
        _lastFrame = now;

        // Dopo una pausa - finestra ridotta a icona, macchina sotto sforzo - il
        // salto sarebbe enorme e le papere si teletrasporterebbero.
        Step(Math.Min(elapsed, 50) / FrameMilliseconds);
    }

    private void Step(double pace)
    {
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0 || _ducks.Count == 0)
        {
            return;
        }

        Separate();

        foreach (Duck duck in _ducks)
        {
            duck.Wobble += duck.WobbleSpeed * pace;
            duck.X += duck.VX * pace;
            duck.Y += (duck.VY + (Math.Sin(duck.Wobble) * 0.25)) * pace;

            Bounce(duck, width, height);
            Place(duck);
        }
    }

    private void Separate()
    {
        for (int i = 0; i < _ducks.Count; i++)
        {
            for (int j = i + 1; j < _ducks.Count; j++)
            {
                Duck a = _ducks[i], b = _ducks[j];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                double minimum = (a.Size + b.Size) / 2.2;

                if (distance >= minimum || distance <= 0)
                {
                    continue;
                }

                double nx = dx / distance, ny = dy / distance;
                double overlap = (minimum - distance) / 2;

                a.X -= nx * overlap;
                a.Y -= ny * overlap;
                b.X += nx * overlap;
                b.Y += ny * overlap;

                double closing = ((a.VX - b.VX) * nx) + ((a.VY - b.VY) * ny);
                if (closing <= 0)
                {
                    continue;
                }

                a.VX -= closing * nx;
                a.VY -= closing * ny;
                b.VX += closing * nx;
                b.VY += closing * ny;
            }
        }
    }

    private static void Bounce(Duck duck, double width, double height)
    {
        double half = duck.Size / 2;

        if (duck.X < -half) { duck.X = -half; duck.VX = Math.Abs(duck.VX); }
        if (duck.X > width + half) { duck.X = width + half; duck.VX = -Math.Abs(duck.VX); }
        if (duck.Y < -half) { duck.Y = -half; duck.VY = Math.Abs(duck.VY); }
        if (duck.Y > height + half) { duck.Y = height + half; duck.VY = -Math.Abs(duck.VY); }
    }

    /// <summary>
    /// Posizione e rotazione passano per la TRASFORMAZIONE, mai per
    /// Canvas.SetLeft.
    /// </summary>
    /// <remarks>
    /// Canvas.Left e' una proprieta' di dipendenza che invalida la disposizione:
    /// ventidue papere per fotogramma significano ventidue passaggi di layout
    /// prima di ogni disegno, ed e' da li' che veniva lo scatto. Una
    /// RenderTransform salta misura e disposizione e arriva diritta al
    /// compositore. La scala non cambia mai dopo la nascita: si imposta in
    /// Build e non si tocca piu'.
    /// </remarks>
    private static void Place(Duck duck)
    {
        duck.Rotate.Angle = Math.Sin(duck.Wobble) * 5;
        duck.Move.X = duck.X - (DuckWidth / 2);
        duck.Move.Y = duck.Y - (DuckHeight / 2);
    }

    private static ImageBrush? LoadMask()
    {
        if (Assets.DuckImage.Bitmap is not { } duck)
        {
            return null;
        }

        ImageBrush mask = new(duck) { Stretch = Stretch.Fill };
        mask.Freeze();
        return mask;
    }
}
