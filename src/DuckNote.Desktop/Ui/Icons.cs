using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Le icone di DuckNote, disegnate a tratto su una griglia di 16x16: niente font di icone, che
/// su macOS e Linux non c'e' (Segoe Fluent Icons e' solo di Windows). Il tratto prende il colore
/// del testo intorno, come un glifo.
/// </summary>
public static class Icons
{
    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["lock"] = "M4,7.5 H12 V14 H4 Z M5.5,7.5 V5 A2.5,2.5 0 0 1 10.5,5 V7.5 M8,10 V11.5",
        ["unlock"] = "M4,7.5 H12 V14 H4 Z M5.5,7.5 V5 A2.5,2.5 0 0 1 10.4,4.2 M8,10 V11.5",
        ["settings"] = "M6.9,1.6 H9.1 L9.5,3.4 L10.9,4.2 L12.7,3.6 L13.8,5.5 L12.4,6.8 V8.4 L13.8,9.7 L12.7,11.6 L10.9,11 L9.5,11.8 L9.1,13.6 H6.9 L6.5,11.8 L5.1,11 L3.3,11.6 L2.2,9.7 L3.6,8.4 V6.8 L2.2,5.5 L3.3,3.6 L5.1,4.2 L6.5,3.4 Z M8,5.6 A2,2 0 1 1 7.99,5.6 Z",
        ["moon"] = "M10,2 A6.3,6.3 0 1 0 14.2,10.2 A5,5 0 1 1 10,2 Z",
        ["sun"] = "M5.2,8 A2.8,2.8 0 1 1 10.8,8 A2.8,2.8 0 1 1 5.2,8 Z M8,1 V2.6 M8,13.4 V15 M1,8 H2.6 M13.4,8 H15 M3,3 L4.1,4.1 M11.9,11.9 L13,13 M13,3 L11.9,4.1 M4.1,11.9 L3,13",
        ["sidebar"] = "M1.5,2.5 H14.5 V13.5 H1.5 Z M6,2.5 V13.5 M3.2,5.5 H4.3 M3.2,8 H4.3",
        ["search"] = "M7,2.5 A4.5,4.5 0 1 1 6.99,2.5 Z M10.4,10.4 L14,14",
        ["add"] = "M8,3 V13 M3,8 H13",
        ["network"] = "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M1.5,8 H14.5 M8,1.5 C5.6,4 5.6,12 8,14.5 M8,1.5 C10.4,4 10.4,12 8,14.5",
        ["map"] = "M2.5,2.5 H6 V6 H2.5 Z M10,2.5 H13.5 V6 H10 Z M6.25,10 H9.75 V13.5 H6.25 Z M6,4.25 H10 M4.25,6 L7,10 M11.75,6 L9,10",
        ["undo"] = "M5,3.5 L2,6.5 L5,9.5 M2,6.5 H10 A3.5,3.5 0 0 1 10,13.5 H6.5",
        ["redo"] = "M11,3.5 L14,6.5 L11,9.5 M14,6.5 H6 A3.5,3.5 0 0 0 6,13.5 H9.5",
        ["bullet"] = "M6,3.5 H14 M6,8 H14 M6,12.5 H14 M2.5,3.5 h0.1 M2.5,8 h0.1 M2.5,12.5 h0.1",
        ["number"] = "M1.5,2 H2.6 V6 M1.5,6 H3.6 M1.5,8.8 H3.6 L1.5,12.4 H3.6 M6,3.5 H14 M6,8 H14 M6,12.5 H14",
        ["todo"] = "M2,2.5 H13.5 V14 H2 Z M4.8,8.3 L7,10.5 L11,5.9",
        ["quote"] = "M2.5,2 V14 M6,4 H14 M6,8 H14 M6,12 H11",
        ["code"] = "M5,4 L1.5,8 L5,12 M11,4 L14.5,8 L11,12 M9.4,2.5 L6.6,13.5",
        ["rule"] = "M1,8 H15",
        ["table"] = "M1.5,2.5 H14.5 V13.5 H1.5 Z M1.5,6 H14.5 M1.5,9.75 H14.5 M6,2.5 V13.5 M10.5,2.5 V13.5",
        ["link"] = "M6.5,9.5 L9.5,6.5 M7.2,4.6 L8.9,2.9 A2.8,2.8 0 0 1 12.9,6.9 L11.2,8.6 M8.8,11.4 L7.1,13.1 A2.8,2.8 0 0 1 3.1,9.1 L4.8,7.4",
        ["image"] = "M1.5,2.5 H14.5 V13.5 H1.5 Z M1.5,11 L5.5,7 L8.5,10 L10.5,8 L14.5,12 M10.6,5.4 h0.1",
        ["drawing"] = "M1,12 C4,4 6,14 9,7 C11,3 13,6 15,3",
        ["chart"] = "M1.5,14 H14.5 M3.5,14 V9 M7,14 V4 M10.5,14 V7 M14,14 V2.5",
        ["clear"] = "M3,13.5 H13 M4,9 L10,2 L14,6 L9,12 M4,9 L7,12 L9,12",
        ["more"] = "M3,8 h0.1 M8,8 h0.1 M13,8 h0.1",
        ["close"] = "M3.5,3.5 L12.5,12.5 M12.5,3.5 L3.5,12.5",
        ["down"] = "M3.5,6 L8,10.5 L12.5,6",
        ["up"] = "M3.5,10 L8,5.5 L12.5,10",
        ["right"] = "M6,3.5 L10.5,8 L6,12.5",
        ["left"] = "M10,3.5 L5.5,8 L10,12.5",
        ["refresh"] = "M13.5,8 A5.5,5.5 0 1 1 11.9,4.1 M12.3,1.5 V4.5 H9.3",
        ["play"] = "M4.5,2.5 L13,8 L4.5,13.5 Z",
        ["stop"] = "M4,4 H12 V12 H4 Z",
        ["export"] = "M8,2 V10 M4.5,6.5 L8,10 L11.5,6.5 M2.5,11.5 V14 H13.5 V11.5",
        ["copy"] = "M5.5,5.5 H13.5 V14.5 H5.5 Z M3,10.5 H2.5 V1.5 H10.5 V2.5",
        ["hide"] = "M1.5,8 C3.5,4.5 12.5,4.5 14.5,8 C12.5,11.5 3.5,11.5 1.5,8 Z M8,6.2 A1.8,1.8 0 1 1 7.99,6.2 Z M2.5,13.5 L13.5,2.5",
        ["show"] = "M1.5,8 C3.5,4.5 12.5,4.5 14.5,8 C12.5,11.5 3.5,11.5 1.5,8 Z M8,6.2 A1.8,1.8 0 1 1 7.99,6.2 Z",
        ["info"] = "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M8,7 V11.5 M8,4.6 h0.1",
        ["warning"] = "M8,2 L14.5,13.5 H1.5 Z M8,6.5 V9.5 M8,11.6 h0.1",
        ["error"] = "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M5.6,5.6 L10.4,10.4 M10.4,5.6 L5.6,10.4",
        ["check"] = "M3,8.5 L6.5,12 L13,4.5",
        ["trash"] = "M2.5,4 H13.5 M6,4 V2.5 H10 V4 M4,4 L4.7,14 H11.3 L12,4 M6.6,6.5 V11.5 M9.4,6.5 V11.5",
        ["folder"] = "M1.5,3.5 H6 L7.5,5 H14.5 V13.5 H1.5 Z",
        ["key"] = "M5.2,5.2 A3,3 0 1 1 5.19,5.2 Z M7.6,9.6 L13.8,9.6 M11.5,9.6 V12 M13.8,9.6 V11.6",
        ["shield"] = "M8,1.5 L13.5,3.5 V8 C13.5,11.5 10.5,13.5 8,14.5 C5.5,13.5 2.5,11.5 2.5,8 V3.5 Z",
        ["user"] = "M8,2 A3,3 0 1 1 7.99,2 Z M2.5,14.5 C3,10.5 13,10.5 13.5,14.5",
        ["note"] = "M2,14 L3,10.5 L11,2.5 L13.5,5 L5.5,13 Z M9.5,4 L12,6.5",
        ["pulse"] = "M1,8 H4 L6,3 L10,13 L12,8 H15",
        ["open"] = "M9,2 H14 V7 M14,2 L7.5,8.5 M12,9.5 V14 H2 V4 H6.5",
        ["exit"] = "M10,4.5 V2 H2.5 V14 H10 V11.5 M6.5,8 H14.5 M12,5.5 L14.5,8 L12,10.5",
        ["dot"] = "M8,4 A4,4 0 1 1 7.99,4 Z",
        ["web"] = "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M1.5,8 H14.5",
        ["swap"] = "M2,5 H13 M10.5,2.5 L13,5 L10.5,7.5 M14,11 H3 M5.5,8.5 L3,11 L5.5,13.5",
        ["highlight"] = "M3,13.5 H13 M4.5,11 L5.5,8 L10.5,3 L13,5.5 L8,10.5 Z",
        ["group"] = "M8,1.8 L14.5,5 L8,8.2 L1.5,5 Z M1.5,8 L8,11.2 L14.5,8 M1.5,11 L8,14.2 L14.5,11",
        ["calendar"] = "M2,3.5 H14 V14 H2 Z M2,6.5 H14 M5,1.8 V4.8 M11,1.8 V4.8 M5,9.2 h0.1 M8,9.2 h0.1 M11,9.2 h0.1 M5,11.8 h0.1 M8,11.8 h0.1",
        ["bell"] = "M4,11.2 V7 A4,4 0 0 1 12,7 V11.2 L13.5,12.5 H2.5 Z M6.6,14.3 A1.6,1.6 0 0 0 9.4,14.3",
        ["edit"] = "M2,14 L3,10.5 L11,2.5 L13.5,5 L5.5,13 Z M9.5,4 L12,6.5",
    };

    private static readonly Dictionary<string, Geometry> Parsed = new(StringComparer.Ordinal);

    public static Geometry? Geometry(string? name)
    {
        if (name is null || !Paths.TryGetValue(name, out string? data))
        {
            return null;
        }

        if (!Parsed.TryGetValue(name, out Geometry? geometry))
        {
            geometry = StreamGeometry.Parse(data);
            Parsed[name] = geometry;
        }

        return geometry;
    }
}

/// <summary>Un'icona di <see cref="Icons"/>: un tratto arrotondato col colore del testo intorno.</summary>
public sealed class Glyph : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<Glyph, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<Glyph, double>(nameof(Thickness), 1.45);

    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<Glyph, bool>(nameof(Filled));

    static Glyph()
    {
        AffectsRender<Glyph>(KindProperty, ForegroundProperty, ThicknessProperty, FilledProperty);
    }

    public Glyph()
    {
        Width = 14;
        Height = 14;
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Icons.Geometry(Kind) is not { } geometry || Foreground is not { } ink || Bounds.Width <= 0)
        {
            return;
        }

        double scale = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
        double offsetX = (Bounds.Width - 16 * scale) / 2;
        double offsetY = (Bounds.Height - 16 * scale) / 2;

        // i punti (h0.1) diventano pallini grazie alle estremita' tonde: un tratto un po' piu' spesso
        double thickness = Kind is "more" or "dot" ? Thickness * 1.6 : Thickness;
        Pen pen = new(ink, thickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(Filled || Kind is "play" or "stop" or "dot" ? ink : null, pen, geometry);
        }
    }
}
