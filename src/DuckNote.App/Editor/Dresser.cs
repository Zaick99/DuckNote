using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed class Dresser(EditorBrushes brushes)
{
    public const string MonoFonts = "SF Mono, Cascadia Mono, Consolas, Menlo, Courier New";

    /// <summary>
    /// Lo spazio sopra e sotto il filo della separazione. Sommato al filo fa
    /// l'altezza di una riga di testo: una separazione occupa una riga, come
    /// tutto il resto di quel che si scrive.
    /// </summary>
    private const double RuleAir = 6.5;

    private static readonly DependencyProperty[] Worn =
    [
        TextElement.FontSizeProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStyleProperty,
        TextElement.FontFamilyProperty,
        TextElement.ForegroundProperty,
        TextElement.BackgroundProperty,
        Block.BorderBrushProperty,
        Block.BorderThicknessProperty,
        Block.PaddingProperty,
        Block.LineHeightProperty,
        Block.LineStackingStrategyProperty,
        Paragraph.ClearFloatersProperty,
        Paragraph.TextIndentProperty
    ];

    public EditorBrushes Palette => brushes;

    public Paragraph CodeBlock(IEnumerable<string> lines)
    {
        Paragraph block = NoteDocument.Row();
        bool first = true;

        foreach (string line in lines)
        {
            if (!first)
            {
                block.Inlines.Add(new LineBreak());
            }

            block.Inlines.Add(new Run(line));
            first = false;
        }

        if (first)
        {
            block.Inlines.Add(new Run(string.Empty));
        }

        Dress(block, Look.Code);

        return block;
    }

    public void Dress(Paragraph row, Look look, int number = 1)
    {
        Undress(row);

        switch (look)
        {
            case Look.Heading1 or Look.Heading2 or Look.Heading3:
                row.FontSize = Looks.HeadingSizes[look - Look.Heading1];
                row.FontWeight = FontWeights.SemiBold;
                break;

            case Look.Quote:
                row.BorderBrush = brushes.Tertiary;
                row.BorderThickness = new Thickness(2, 0, 0, 0);
                row.Padding = Looks.Indent(Look.Quote);
                row.FontStyle = FontStyles.Italic;
                row.Foreground = brushes.Secondary;
                break;

            // Il respiro di una riga di separazione sta nei margini, non nei
            // bordi interni: il bordo e' sempre l'ultima cosa del paragrafo,
            // quindi col solo padding lo spazio finisce tutto sopra e la riga
            // resta incollata al testo che segue. La riga vuota sotto non
            // serve, quindi l'altezza di riga va messa a uno.
            // Il bordo di un paragrafo si disegna per tutta la sua larghezza,
            // riquadri che galleggiano compresi: una separazione lasciata
            // accanto a un'immagine la taglierebbe a meta'. ClearFloaters la
            // manda sotto, dove una separazione ha senso.
            case Look.Rule:
                row.Inlines.Clear();
                row.BorderBrush = brushes.Hairline;
                row.BorderThickness = new Thickness(0, 0, 0, 1);
                row.LineHeight = 1;
                row.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                row.Margin = new Thickness(0, RuleAir, 0, RuleAir);
                row.ClearFloaters = WrapDirection.Both;
                break;

            case Look.Bullet:
                Mark(row, Looks.Dot);
                break;

            case Look.Number:
                Mark(row, Looks.Counted(number));
                break;

            case Look.Todo:
                Mark(row, Looks.Unchecked);
                break;

            case Look.Code:
                row.Background = brushes.CodeBackground;
                row.Foreground = brushes.CodeForeground;
                row.FontFamily = new FontFamily(MonoFonts);
                row.Padding = Looks.Indent(Look.Code);
                break;

            default:
                break;
        }
    }

    public void Undress(Paragraph row)
    {
        // Una spunta fatta e' l'unica veste che dipinge il TESTO e non il
        // paragrafo: grigio e barrato stanno sui Run, quindi svestire il
        // paragrafo non li porterebbe via e resterebbero addosso al prossimo
        // tipo di riga.
        if (Looks.IsDone(row))
        {
            Unmarked(row);
        }

        int mark = Looks.MarkLength(row);
        if (mark > 0)
        {
            Strip(row, mark);
        }

        foreach (DependencyProperty property in Worn)
        {
            row.ClearValue(property);
        }

        // Il margine non si azzera svuotandolo: un Paragraph nasce con margine
        // Auto, e Auto vuol dire un buco fra le righe.
        row.Margin = NoteDocument.Flush;
    }

    public void Check(Paragraph row, bool done)
    {
        if (!Looks.Is(row, Look.Todo))
        {
            return;
        }

        Strip(row, Looks.Unchecked.Length);
        Mark(row, done ? Looks.Checked : Looks.Unchecked);

        bool first = true;
        foreach (Run run in row.Inlines.OfType<Run>())
        {
            if (first)
            {
                run.Foreground = done ? brushes.Up : brushes.Secondary;
                first = false;
                continue;
            }

            run.TextDecorations = done ? TextDecorations.Strikethrough : null;
            run.Foreground = done ? brushes.Done : brushes.Text;
        }
    }

    public Run Plain(string text) => new(text) { Foreground = brushes.Text };

    private void Mark(Paragraph row, string mark)
    {
        Run sign = Plain(mark);
        sign.Foreground = brushes.Tertiary;
        sign.FontFamily = new FontFamily(MonoFonts);
        sign.Cursor = Cursors.Hand;

        if (row.Inlines.FirstInline is { } first)
        {
            row.Inlines.InsertBefore(first, sign);
        }
        else
        {
            row.Inlines.Add(sign);
        }

        row.Padding = Looks.Indent(Look.Bullet);
        row.TextIndent = -Looks.Indent(Look.Bullet).Left;
    }

    private void Unmarked(Paragraph row)
    {
        foreach (Run word in row.Inlines.OfType<Run>())
        {
            word.TextDecorations = null;
            word.Foreground = brushes.Text;
        }
    }

    private static void Strip(Paragraph row, int length)
    {
        if (row.Inlines.FirstInline is not Run first)
        {
            return;
        }

        if (first.Text.Length <= length)
        {
            row.Inlines.Remove(first);
        }
        else
        {
            first.Text = first.Text[length..];
        }

        row.ClearValue(Paragraph.TextIndentProperty);
    }
}
