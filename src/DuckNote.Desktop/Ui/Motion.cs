using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Movimento: una tween a fotogrammi per quello che le transizioni di Avalonia non sanno
/// animare (la larghezza di una colonna), e due gesti ricorrenti, comparire e sparire.
/// Ogni movimento si puo' interrompere a meta': il successivo riparte da dove si vede.
/// </summary>
public static class Motion
{
    public static readonly Easing Smooth = new CubicEaseOut();

    private sealed class Run
    {
        public bool Stopped;
    }

    private static readonly Dictionary<object, Run> Running = [];

    /// <summary>
    /// Porta un valore da <paramref name="from"/> a <paramref name="to"/> in <paramref name="ms"/>
    /// millisecondi, chiamando <paramref name="step"/> a ogni fotogramma. Un'altra tween con la
    /// stessa chiave ferma questa.
    /// </summary>
    public static void Tween(object key, double from, double to, int ms, Action<double> step, Action? done = null, Easing? easing = null)
    {
        if (Running.TryGetValue(key, out Run? previous))
        {
            previous.Stopped = true;
        }

        if (ms <= 0 || Math.Abs(from - to) < 0.01)
        {
            Running.Remove(key);
            step(to);
            done?.Invoke();
            return;
        }

        Run run = new();
        Running[key] = run;
        easing ??= Smooth;
        DateTime start = DateTime.UtcNow;
        DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };

        timer.Tick += (_, _) =>
        {
            if (run.Stopped)
            {
                timer.Stop();
                return;
            }

            double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / ms);
            step(from + (to - from) * easing.Ease(t));

            if (t >= 1)
            {
                timer.Stop();
                Running.Remove(key);
                done?.Invoke();
            }
        };

        step(from);
        timer.Start();
    }

    /// <summary>Compare salendo un poco, in dissolvenza.</summary>
    public static void Show(Control control, double rise = 10, int ms = 260)
    {
        control.IsVisible = true;
        TranslateTransform shift = control.RenderTransform as TranslateTransform ?? new TranslateTransform();
        control.RenderTransform = shift;
        Tween((control, "show"), 0, 1, ms, t =>
        {
            control.Opacity = t;
            shift.Y = rise * (1 - t);
        });
    }

    /// <summary>Sparisce scendendo un poco, in dissolvenza; alla fine non occupa piu' spazio.</summary>
    public static void Hide(Control control, double fall = 10, int ms = 180, Action? done = null)
    {
        if (!control.IsVisible)
        {
            done?.Invoke();
            return;
        }

        TranslateTransform shift = control.RenderTransform as TranslateTransform ?? new TranslateTransform();
        control.RenderTransform = shift;
        double from = control.Opacity;
        Tween((control, "show"), from, 0, ms, t =>
        {
            control.Opacity = t;
            shift.Y = fall * (1 - t);
        }, () =>
        {
            control.IsVisible = false;
            done?.Invoke();
        }, new CubicEaseIn());
    }
}
