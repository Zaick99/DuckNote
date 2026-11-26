using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// La rotella dell'attesa, come lo Spinner della versione WPF: un arco d'accento su un anello
/// grigio, che gira (un giro in 950 ms) e intanto si allunga e si accorcia fra 40 e 260 gradi
/// (1100 ms, avanti e indietro). Gira solo quando si vede.
/// </summary>
public sealed class WaitRing : Control
{
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private Avalonia.Threading.DispatcherTimer? _timer;

    public double Thickness { get; set; } = 4;

    public WaitRing()
    {
        AttachedToVisualTree += (_, _) => Run();
        DetachedFromVisualTree += (_, _) => Run();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                Run();
            }
        };
    }

    private void Run()
    {
        bool on = IsVisible && VisualRoot is not null;

        if (on && _timer is null)
        {
            _clock.Restart();
            _timer = new Avalonia.Threading.DispatcherTimer(Avalonia.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => InvalidateVisual();
            _timer.Start();
        }
        else if (!on && _timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);

        if (size < Thickness * 3)
        {
            return;
        }

        double ms = _clock.Elapsed.TotalMilliseconds;
        double turn = ms % 950 / 950 * 2 * Math.PI;
        double phase = ms % 2200 / 1100;
        double swing = Curves.SineInOut(phase <= 1 ? phase : 2 - phase);
        double sweep = (40 + (220 * swing)) * Math.PI / 180;

        Point centre = new(Bounds.Width / 2, Bounds.Height / 2);
        double radius = (size / 2) - (Thickness / 2);
        context.DrawEllipse(null, new Pen(Brush("SeparatorSoft", Brushes.LightGray), Thickness), centre, radius, radius);

        Point start = new(centre.X + (radius * Math.Sin(turn)), centre.Y - (radius * Math.Cos(turn)));
        Point end = new(centre.X + (radius * Math.Sin(turn + sweep)), centre.Y - (radius * Math.Cos(turn + sweep)));
        StreamGeometry arc = new();

        using (StreamGeometryContext path = arc.Open())
        {
            path.BeginFigure(start, false);
            path.ArcTo(end, new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise);
            path.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(Brush("Accent", Brushes.Orange), Thickness, lineCap: PenLineCap.Round), arc);
    }

    private IBrush Brush(string token, IBrush fallback) =>
        this.TryFindResource(token, ActualThemeVariant, out object? found) && found is IBrush brush ? brush : fallback;
}
