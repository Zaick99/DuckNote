using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Le curve della versione WPF, scritte a mano con le stesse formule di ExponentialEase,
/// ElasticEase, BackEase, CubicEase e SineEase: niente curve gia' pronte, i movimenti hanno lo
/// stesso carattere che hanno su Windows. Ogni curva va da 0 a 1; le elastiche e "back" escono
/// un poco oltre l'1 prima di posarsi.
/// </summary>
public static class Curves
{
    public static double Linear(double t) => t;

    public static double CubicIn(double t) => t * t * t;

    public static readonly Func<double, double> CubicOut = Out(CubicIn);

    public static readonly Func<double, double> CubicInOut = InOut(CubicIn);

    public static double SineIn(double t) => 1 - Math.Sin(Math.PI * 0.5 * (1 - t));

    public static readonly Func<double, double> SineInOut = InOut(SineIn);

    /// <summary>ExponentialEase in uscita: parte deciso e si posa.</summary>
    public static Func<double, double> ExpoOut(double exponent = 4) =>
        Out(t => (Math.Exp(exponent * t) - 1) / (Math.Exp(exponent) - 1));

    /// <summary>ElasticEase in uscita: arriva, passa oltre, torna, con le oscillazioni chieste.</summary>
    public static Func<double, double> ElasticOut(double oscillations = 1, double springiness = 6) =>
        Out(t =>
        {
            double expo = springiness <= 0 ? t : (Math.Exp(springiness * t) - 1) / (Math.Exp(springiness) - 1);
            return expo * Math.Sin(((2 * Math.PI * oscillations) + (Math.PI * 0.5)) * t);
        });

    /// <summary>BackEase in uscita: supera di poco l'arrivo e ci torna.</summary>
    public static Func<double, double> BackOut(double amplitude = 0.35) =>
        Out(t => (t * t * t) - (t * amplitude * Math.Sin(Math.PI * t)));

    /// <summary>Da una curva "in" la sua uscita, come EasingMode.EaseOut di WPF.</summary>
    public static Func<double, double> Out(Func<double, double> easeIn) => t => 1 - easeIn(1 - t);

    /// <summary>Entrata e uscita, come EasingMode.EaseInOut di WPF.</summary>
    public static Func<double, double> InOut(Func<double, double> easeIn) =>
        t => t < 0.5 ? easeIn(t * 2) * 0.5 : ((1 - easeIn((1 - t) * 2)) * 0.5) + 0.5;
}

/// <summary>
/// Movimento: una tween a fotogrammi (circa 120 al secondo, come la versione WPF) e i gesti
/// ricorrenti, comparire e sparire. Ogni movimento si puo' interrompere a meta': il successivo
/// con la stessa chiave riparte da dove si vede.
/// </summary>
public static class Motion
{
    private sealed class Run
    {
        public bool Stopped;
    }

    private static readonly Dictionary<object, Run> Running = [];

    /// <summary>C'e' una tween in corso con questa chiave.</summary>
    public static bool IsRunning(object key) => Running.ContainsKey(key);

    /// <summary>Ferma la tween con questa chiave dove si trova.</summary>
    public static void Stop(object key)
    {
        if (Running.Remove(key, out Run? run))
        {
            run.Stopped = true;
        }
    }

    /// <summary>
    /// Porta un valore da <paramref name="from"/> a <paramref name="to"/> in <paramref name="ms"/>
    /// millisecondi lungo <paramref name="curve"/> (di serie l'uscita cubica), chiamando
    /// <paramref name="step"/> a ogni fotogramma; con <paramref name="delay"/> resta fermo su
    /// <paramref name="from"/> per quei millisecondi prima di partire.
    /// </summary>
    public static void Tween(object key, double from, double to, int ms, Action<double> step, Action? done = null, Func<double, double>? curve = null, int delay = 0)
    {
        Stop(key);

        if (ms <= 0 || Math.Abs(from - to) < 0.0001)
        {
            step(to);
            done?.Invoke();
            return;
        }

        Run run = new();
        Running[key] = run;
        curve ??= Curves.CubicOut;
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };

        timer.Tick += (_, _) =>
        {
            if (run.Stopped)
            {
                timer.Stop();
                return;
            }

            double t = Math.Clamp((clock.Elapsed.TotalMilliseconds - delay) / ms, 0, 1);
            step(from + ((to - from) * curve(t)));

            if (t >= 1)
            {
                timer.Stop();

                if (Running.TryGetValue(key, out Run? current) && ReferenceEquals(current, run))
                {
                    Running.Remove(key);
                }

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

    /// <summary>
    /// Le righe di un elenco che entrano una dopo l'altra: dissolvenza e uno scivolo da destra di
    /// <paramref name="slide"/> pixel, ognuna <paramref name="step"/> ms dopo la precedente, al
    /// massimo le prime <paramref name="max"/> (le altre ci sono gia').
    /// </summary>
    public static void Cascade(IEnumerable<Control> items, int step = 22, int max = 14, int ms = 320, double slide = 16, int start = 0)
    {
        int index = 0;

        foreach (Control item in items.Take(max))
        {
            Arrive(item, ms, slide, start + (step * index++));
        }
    }

    /// <summary>Una riga che arriva: dissolvenza e uno scivolo da destra, dopo <paramref name="delay"/> ms.</summary>
    public static void Arrive(Control item, int ms = 300, double slide = 16, int delay = 0)
    {
        TranslateTransform shift = item.RenderTransform as TranslateTransform ?? new TranslateTransform();
        item.RenderTransform = shift;
        item.Opacity = 0;
        shift.X = slide;
        Tween((item, "arrive"), 0, 1, ms, t =>
        {
            item.Opacity = t;
            shift.X = slide * (1 - t);
        }, () =>
        {
            item.Opacity = 1;
            shift.X = 0;
        }, delay: delay);
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
        }, Curves.CubicIn);
    }
}
