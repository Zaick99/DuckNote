using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DuckNote.App.Editor;

/// <summary>
/// I tastini dentro i riquadri. Nascono fuori da ogni albero, quindi lo stile
/// della barra si trova solo quando il tasto entra nella finestra.
/// </summary>
internal static class Chips
{
    public static Button Make(string label, Action press)
    {
        Button tap = new()
        {
            Content = label,
            FontSize = 10.5,
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand
        };

        tap.Click += (_, _) => press();
        Dressed(tap, "Toolbtn");

        return tap;
    }

    /// <summary>
    /// Il tasto per togliere il riquadro: lo stesso pallino rosso col quale si
    /// chiudono le finestre, che mostra la croce quando ci passi sopra.
    /// </summary>
    public static Button Close(Action press) =>
        Dot(press, "TLClose", Color.FromRgb(0x5A, 0x14, 0x16), "M 0,0 L 6,6 M 6,0 L 0,6", "Togli il riquadro");

    /// <summary>I numeri del grafico: pallino viola, tre barrette dentro.</summary>
    public static Button Numbers(Action press) =>
        Dot(press, "Purple", Color.FromRgb(0x24, 0x18, 0x50), "M 0,6 V 2 M 3,6 V 0 M 6,6 V 3", "I numeri del grafico");

    private static Button Dot(Action press, string token, Color ink, string glyph, string tip)
    {
        Button tap = new()
        {
            Content = Drawn(glyph, ink),
            Width = 12,
            Height = 12,
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tip
        };

        tap.Click += (_, _) => press();
        Dressed(tap, "TrafficBtn");

        tap.Loaded += (_, _) =>
        {
            if (tap.TryFindResource(token) is Brush paint)
            {
                tap.Background = paint;
            }
        };

        return tap;
    }

    private static Path Drawn(string glyph, Color ink) => new()
    {
        Data = Geometry.Parse(glyph),
        Stroke = new SolidColorBrush(ink),
        StrokeThickness = 1.2,
        Stretch = Stretch.Uniform,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    private static void Dressed(Button tap, string look) =>
        tap.Loaded += (_, _) =>
        {
            if (tap.TryFindResource(look) is Style found)
            {
                tap.Style = found;
            }
        };
}
