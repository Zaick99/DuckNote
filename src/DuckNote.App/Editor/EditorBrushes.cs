using System.Windows.Media;
using DuckNote.App.Theme;

namespace DuckNote.App.Editor;

public sealed class EditorBrushes
{
    public SolidColorBrush Text { get; } = new();
    public SolidColorBrush Secondary { get; } = new();
    public SolidColorBrush Tertiary { get; } = new();
    public SolidColorBrush CodeBackground { get; } = new();
    public SolidColorBrush CodeForeground { get; } = new();

    // I colori della lingua dentro un blocco di codice.
    public SolidColorBrush CodeWord { get; } = new();
    public SolidColorBrush CodeQuote { get; } = new();
    public SolidColorBrush CodeNumber { get; } = new();
    public SolidColorBrush CodeRemark { get; } = new();
    public SolidColorBrush CodeCall { get; } = new();

    public SolidColorBrush Link { get; } = new();
    public SolidColorBrush Up { get; } = new();
    public SolidColorBrush Down { get; } = new();
    public SolidColorBrush Unknown { get; } = new();
    public SolidColorBrush TableLine { get; } = new();
    public SolidColorBrush Hairline { get; } = new();
    public SolidColorBrush Accent { get; } = new();
    public SolidColorBrush TableHeader { get; } = new();
    public SolidColorBrush MarkBackground { get; } = new();
    public SolidColorBrush MarkForeground { get; } = new();
    public SolidColorBrush Done { get; } = new();

    /// <summary>I colori delle serie di un grafico, in ordine di comparsa.</summary>
    public SolidColorBrush[] Series { get; } = [new(), new(), new(), new(), new(), new(), new()];

    private static readonly string[] SeriesTokens =
        ["Blue", "Green", "Orange", "Purple", "Teal", "Pink", "Yellow"];

    public void Sync(string theme)
    {
        for (int i = 0; i < Series.Length; i++)
        {
            Series[i].Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, SeriesTokens[i]));
        }

        Text.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Label"));
        Secondary.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelSecondary"));
        Tertiary.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelTertiary"));
        CodeBackground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "CodeBg"));
        CodeForeground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "CodeFg"));

        // Cinque ruoli, cinque colori gia' del tema: niente tinte inventate
        // apposta, cosi' il codice resta leggibile col tema chiaro e con quello
        // scuro senza una seconda tavolozza da tenere in riga.
        CodeWord.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Purple"));
        CodeQuote.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Green"));
        CodeNumber.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Orange"));
        CodeRemark.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelTertiary"));
        CodeCall.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Blue"));
        Link.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Blue"));
        Up.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Green"));
        Down.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Red"));
        Unknown.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelQuaternary"));
        TableLine.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "TableBorder"));
        Hairline.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Separator"));
        Accent.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Accent"));
        TableHeader.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "TableHeaderBg"));
        MarkBackground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Highlight"));
        MarkForeground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "HighlightFg"));
        Done.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelTertiary"));
    }

    public SolidColorBrush ForHost(string name, IReadOnlyDictionary<string, bool> states) =>
        states.TryGetValue(name, out bool up) ? (up ? Up : Down) : Unknown;
}
