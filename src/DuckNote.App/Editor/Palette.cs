using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DuckNote.App.Theme;

namespace DuckNote.App.Editor;

/// <summary>
/// I colori della nota sono i pennelli vivi del tema: cambiando tema cambiano da soli, senza
/// rifare il documento. Una nota letta da disco porta invece colori scritti (il salvataggio in
/// XAML fotografa il pennello): <see cref="Retint"/> li riconosce e li riallaccia al tema.
/// </summary>
public static class Palette
{
    public static Brush Of(string token) => (Brush)Application.Current.FindResource(token);

    public static Brush Text => Of("Label");
    public static Brush Secondary => Of("LabelSecondary");
    public static Brush Tertiary => Of("LabelTertiary");
    public static Brush Unknown => Of("LabelQuaternary");
    public static Brush CodeBack => Of("CodeBg");
    public static Brush CodeFore => Of("CodeFg");
    public static Brush Link => Of("Blue");
    public static Brush Up => Of("Green");
    public static Brush Down => Of("Red");
    public static Brush TableLine => Of("TableBorder");
    public static Brush TableHead => Of("TableHeaderBg");
    public static Brush Hairline => Of("Separator");
    public static Brush Accent => Of("Accent");
    public static Brush MarkBack => Of("Highlight");
    public static Brush MarkFore => Of("HighlightFg");

    /// <summary>I colori che si possono dare al testo dalla barra: nome mostrato e token del tema.</summary>
    public static readonly (string Name, string Token)[] TextColours =
    [
        ("Automatico", "Label"), ("Grigio", "LabelSecondary"), ("Rosso", "Red"), ("Arancio", "Orange"),
        ("Giallo", "Yellow"), ("Verde", "Green"), ("Verde acqua", "Teal"), ("Blu", "Blue"),
        ("Indaco", "Indigo"), ("Viola", "Purple"), ("Rosa", "Pink")
    ];

    /// <summary>Le tinte delle serie dei grafici, in ordine.</summary>
    public static readonly string[] Series = ["Blue", "Green", "Orange", "Purple", "Teal", "Pink", "Yellow"];

    /// <summary>
    /// I token che il testo puo' portare, in ordine di precedenza: due token con lo stesso colore
    /// in un tema si risolvono a favore del primo.
    /// </summary>
    private static readonly string[] Tinted =
    [
        "Label", "LabelSecondary", "LabelTertiary", "LabelQuaternary", "CodeFg", "CodeBg", "Highlight", "HighlightFg",
        "TableHeaderBg", "TableBorder", "Separator", "Blue", "Green", "Red", "Orange", "Purple", "Teal", "Accent",
        "Pink", "Yellow", "Indigo"
    ];

    private static Dictionary<Color, string>? _known;

    public static void Retint(FlowDocument? document)
    {
        if (document is null)
        {
            return;
        }

        _known ??= Known();

        foreach (Block block in Doc.Deep(document.Blocks))
        {
            Swap(block, TextElement.ForegroundProperty);
            Swap(block, TextElement.BackgroundProperty);
            Swap(block, Block.BorderBrushProperty);

            if (block is Paragraph row)
            {
                foreach (Inline inline in Inlines(row.Inlines))
                {
                    Swap(inline, TextElement.ForegroundProperty);
                    Swap(inline, TextElement.BackgroundProperty);
                }
            }

            if (block is Table table)
            {
                foreach (TableCell cell in table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells))
                {
                    Swap(cell, TextElement.BackgroundProperty);
                    Swap(cell, TableCell.BorderBrushProperty);
                    Swap(cell, TextElement.ForegroundProperty);
                }
            }
        }
    }

    private static IEnumerable<Inline> Inlines(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            yield return inline;

            if (inline is Span span)
            {
                foreach (Inline nested in Inlines(span.Inlines))
                {
                    yield return nested;
                }
            }
        }
    }

    private static void Swap(TextElement element, DependencyProperty property)
    {
        if (element.ReadLocalValue(property) is SolidColorBrush brush
            && _known!.TryGetValue(brush.Color, out string? token)
            && !ReferenceEquals(brush, Of(token)))
        {
            element.SetValue(property, Of(token));
        }
    }

    private static Dictionary<Color, string> Known()
    {
        Dictionary<Color, string> known = [];

        foreach (string theme in (string[])["light", "dark"])
        {
            foreach (string token in Tinted)
            {
                known.TryAdd(ThemeTokens.Parse(ThemeTokens.Colour(theme, token)), token);
            }
        }

        return known;
    }
}
