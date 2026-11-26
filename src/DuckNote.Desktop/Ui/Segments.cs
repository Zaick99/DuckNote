using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Il controllo segmentato con la pillola che scorre, come Seg.Slide nella versione WPF: la
/// pillola e' un elemento solo, dietro alle voci; cambiando scelta scivola con una molla (520
/// ms) e si allarga o si stringe sulla larghezza della nuova voce (380 ms). La prima volta, e
/// quando cambia la misura del controllo, si mette al suo posto senza muoversi.
/// </summary>
public static class Segments
{
    public static Border Well(Thickness margin, params RadioButton[] radios)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.AddRange(radios);

        TranslateTransform slide = new();
        Border pill = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(7),
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransform = slide,
            BoxShadow = BoxShadows.Parse("0 1 6 0 #2E000000")
        };
        pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable("AccentFill"));

        Panel host = new() { Children = { pill, row } };
        Border well = new()
        {
            Padding = new Thickness(3),
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = host
        };
        well.Classes.Add("well");

        RadioButton? Chosen() => radios.FirstOrDefault(radio => radio.IsChecked == true);

        void Move(RadioButton? radio, bool immediate)
        {
            if (radio is null || radio.Bounds.Width <= 0 || !radio.IsVisible)
            {
                if (radio is null)
                {
                    pill.Opacity = 0;
                }

                return;
            }

            double x = radio.Bounds.X;
            double width = radio.Bounds.Width;

            if (immediate || pill.Opacity < 0.5)
            {
                Motion.Stop((pill, "x"));
                Motion.Stop((pill, "width"));
                slide.X = x;
                pill.Width = width;

                if (pill.Opacity < 0.5)
                {
                    Motion.Tween((pill, "fade"), 0, 1, 180, value => pill.Opacity = value, null, Curves.Linear);
                }

                return;
            }

            double fromWidth = double.IsNaN(pill.Width) ? width : pill.Width;
            Motion.Tween((pill, "x"), slide.X, x, 520, value => slide.X = value, null, Curves.ElasticOut(1, 8));
            // a fine corsa la pillola prende la misura vera della voce (il grassetto la allarga)
            Motion.Tween((pill, "width"), fromWidth, width, 380, value => pill.Width = Math.Max(0, value),
                () => Settle(), Curves.ExpoOut(4));
        }

        void Settle()
        {
            if (Chosen() is { } radio && !Motion.IsRunning((pill, "x")) && !Motion.IsRunning((pill, "width")) && pill.Opacity >= 0.5)
            {
                slide.X = radio.Bounds.X;
                pill.Width = radio.Bounds.Width;
            }
        }

        foreach (RadioButton radio in radios)
        {
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                {
                    Move(radio, immediate: false);
                }
            };

            radio.PropertyChanged += (_, e) =>
            {
                if (e.Property != Visual.BoundsProperty || radio.IsChecked != true)
                {
                    return;
                }

                if (pill.Opacity < 0.5)
                {
                    Move(radio, immediate: true);
                }
                else
                {
                    Settle();
                }
            };
        }

        host.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
            {
                Settle();
            }
        };

        return well;
    }
}
