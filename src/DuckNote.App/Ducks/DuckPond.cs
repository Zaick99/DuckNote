using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DuckNote.App.Theme;

namespace DuckNote.App.Ducks;

public sealed class DuckPond(Canvas canvas)
{
    private const double DuckWidth = 512;
    private const double DuckHeight = 512;

    private readonly List<Duck> _ducks = [];
    private readonly Random _random = new();
    private DispatcherTimer? _timer;
    private ImageBrush? _mask;

    private sealed class Duck
    {
        public double X, Y, Size, VX, VY, Wobble, WobbleSpeed;
        public required Shape Shape;
        public required ScaleTransform Scale;
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

            ScaleTransform scale = new() { CenterX = DuckWidth / 2, CenterY = DuckHeight / 2 };
            RotateTransform rotate = new() { CenterX = DuckWidth / 2, CenterY = DuckHeight / 2 };
            TranslateTransform move = new();

            TransformGroup group = new();
            group.Children.Add(scale);
            group.Children.Add(rotate);
            group.Children.Add(move);

            Shape shape = new Rectangle
            {
                Width = DuckWidth,
                Height = DuckHeight,
                OpacityMask = _mask,
                Fill = ink,
                Opacity = opacity,
                RenderTransform = group,
                IsHitTestVisible = false
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
                Scale = scale,
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

    public void Start()
    {
        if (_ducks.Count == 0)
        {
            return;
        }

        _timer ??= CreateTimer();
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private DispatcherTimer CreateTimer()
    {
        DispatcherTimer timer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        timer.Tick += (_, _) => Step();
        return timer;
    }

    private void Step()
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
            duck.Wobble += duck.WobbleSpeed;
            duck.X += duck.VX;
            duck.Y += duck.VY + (Math.Sin(duck.Wobble) * 0.25);

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

    private static void Place(Duck duck)
    {
        double scale = duck.Size / DuckWidth;
        duck.Scale.ScaleX = scale;
        duck.Scale.ScaleY = scale;
        duck.Rotate.Angle = Math.Sin(duck.Wobble) * 5;

        Canvas.SetLeft(duck.Shape, duck.X - (DuckWidth / 2));
        Canvas.SetTop(duck.Shape, duck.Y - (DuckHeight / 2));
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
