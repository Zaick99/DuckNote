using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Il tocco dei pulsanti, come nella versione WPF (Fx.RegisterPress): premuto il pulsante si
/// stringe al 95% in 110 ms, deciso; lasciato torna alla misura con un rimbalzo a molla in 520
/// ms. Vale per i pulsanti del kit Duck (e per gli interruttori della barra); non per i segmenti,
/// che hanno la loro pillola, ne' per quelli piu' stretti di 18 punti.
/// </summary>
public static class Press
{
    private static readonly string[] Kit = ["Primary", "Soft", "Quiet", "Danger", "Tool", "ToolToggle", "Nav"];
    private static HashSet<ControlTheme>? _themes;

    public static void Register()
    {
        // il pulsante gestisce da se' la pressione: si ascolta anche quella gia' gestita
        InputElement.PointerPressedEvent.AddClassHandler<Button>((button, e) =>
        {
            if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                Down(button, true);
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<Button>((button, _) => Down(button, false), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<Button>((button, _) => Down(button, false), handledEventsToo: true);
        InputElement.PointerExitedEvent.AddClassHandler<Button>((button, _) => Down(button, false), handledEventsToo: true);
    }

    private static bool IsKit(Button button)
    {
        if (button is RadioButton || button.Theme is not { } theme)
        {
            return false;
        }

        _themes ??= [.. Kit.Select(Themes.Of).OfType<ControlTheme>()];
        return _themes.Contains(theme) || (theme.BasedOn is ControlTheme parent && _themes.Contains(parent));
    }

    private static void Down(Button button, bool down)
    {
        if (button.Bounds.Width < 18 || !IsKit(button))
        {
            return;
        }

        ScaleTransform? scale = button.RenderTransform switch
        {
            ScaleTransform existing => existing,
            null => new ScaleTransform(1, 1),
            _ => null
        };

        if (scale is null)
        {
            return;
        }

        button.RenderTransformOrigin = RelativePoint.Center;
        button.RenderTransform = scale;

        if (!down && Math.Abs(scale.ScaleX - 1) < 0.001)
        {
            return;
        }

        // giu': rapido e deciso; su: rilascio a molla
        Motion.Tween((button, "press"), scale.ScaleX, down ? 0.95 : 1, down ? 110 : 520, value =>
        {
            scale.ScaleX = value;
            scale.ScaleY = value;
        }, null, down ? Curves.ExpoOut(4) : Curves.ElasticOut(1, 4));
    }
}
