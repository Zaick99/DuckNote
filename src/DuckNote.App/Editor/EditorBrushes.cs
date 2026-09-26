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
    public SolidColorBrush Link { get; } = new();
    public SolidColorBrush Up { get; } = new();
    public SolidColorBrush Down { get; } = new();
    public SolidColorBrush Unknown { get; } = new();
    public SolidColorBrush TableLine { get; } = new();
    public SolidColorBrush TableHeader { get; } = new();
    public SolidColorBrush MarkBackground { get; } = new();
    public SolidColorBrush MarkForeground { get; } = new();
    public SolidColorBrush Done { get; } = new();

    public void Sync(string theme)
    {
        Text.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Label"));
        Secondary.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelSecondary"));
        Tertiary.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelTertiary"));
        CodeBackground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "CodeBg"));
        CodeForeground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "CodeFg"));
        Link.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Blue"));
        Up.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Green"));
        Down.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Red"));
        Unknown.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelQuaternary"));
        TableLine.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "TableBorder"));
        TableHeader.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "TableHeaderBg"));
        MarkBackground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Highlight"));
        MarkForeground.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "HighlightFg"));
        Done.Color = ThemeTokens.Parse(ThemeTokens.Colour(theme, "LabelTertiary"));
    }

    public SolidColorBrush ForHost(string name, IReadOnlyDictionary<string, bool> states) =>
        states.TryGetValue(name, out bool up) ? (up ? Up : Down) : Unknown;
}
