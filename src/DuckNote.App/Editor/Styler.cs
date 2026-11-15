using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DuckNote.App.Editor;

/// <summary>Veste e sveste le righe: titoli, citazioni, separazioni, elenchi, spunte, codice.</summary>
public static class Styler
{
    /// <summary>
    /// Lo spazio sopra e sotto il filo della separazione: filo piu' respiro fanno l'altezza di
    /// una riga di testo, cosi' una separazione occupa una riga come tutto il resto.
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

    public static void Dress(Paragraph row, Look look, int number = 1)
    {
        Undress(row);

        switch (look)
        {
            case Look.Heading1 or Look.Heading2 or Look.Heading3:
                row.FontSize = Looks.HeadingSizes[look - Look.Heading1];
                row.FontWeight = FontWeights.SemiBold;
                break;

            case Look.Quote:
                row.BorderBrush = Palette.Tertiary;
                row.BorderThickness = new Thickness(2, 0, 0, 0);
                row.Padding = Looks.QuoteIndent;
                row.FontStyle = FontStyles.Italic;
                row.Foreground = Palette.Secondary;
                break;

            // Il respiro sta nei margini e l'altezza di riga e' uno: il filo da solo non occupa
            // una riga intera. ClearFloaters la manda sotto i riquadri: un filo che taglia a
            // meta' un'immagine accanto non e' una separazione.
            case Look.Rule:
                row.Inlines.Clear();
                row.BorderBrush = Palette.Hairline;
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
                row.Background = Palette.CodeBack;
                row.Foreground = Palette.CodeFore;
                row.FontFamily = new FontFamily(Doc.MonoFonts);
                row.Padding = Looks.CodePadding;
                break;

            default:
                break;
        }
    }

    public static void Undress(Paragraph row)
    {
        // Una spunta fatta dipinge il testo, non il paragrafo: va tolta dai pezzi, o il grigio
        // barrato resterebbe addosso al prossimo tipo di riga.
        if (Looks.IsDone(row))
        {
            foreach (Run word in row.Inlines.OfType<Run>())
            {
                word.TextDecorations = null;
                word.ClearValue(TextElement.ForegroundProperty);
            }
        }

        if (Looks.MarkLength(row) is var mark and > 0)
        {
            Strip(row, mark);
        }

        foreach (DependencyProperty property in Worn)
        {
            row.ClearValue(property);
        }

        row.Margin = Doc.Flush;
    }

    public static void Check(Paragraph row, bool done)
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
                run.Foreground = done ? Palette.Up : Palette.Tertiary;
                first = false;
                continue;
            }

            run.TextDecorations = done ? TextDecorations.Strikethrough : null;

            if (done)
            {
                run.Foreground = Palette.Tertiary;
            }
            else
            {
                run.ClearValue(TextElement.ForegroundProperty);
            }
        }
    }

    /// <summary>
    /// Rimette d'accordo il rientro con il segno. Cancellando e fondendo righe puo' restare un
    /// rientro da elenco su una riga che il segno non l'ha piu', o un segno su una riga senza
    /// rientro: la riga si sistema da sola invece di restare a meta'. Torna true se ha cambiato.
    /// </summary>
    public static bool Heal(Paragraph row)
    {
        bool hanging = row.ReadLocalValue(Paragraph.TextIndentProperty) != DependencyProperty.UnsetValue && row.TextIndent < 0;
        bool marked = Looks.HasMarkRun(row);

        if (hanging && !marked && Looks.MarkLength(row) == 0)
        {
            row.ClearValue(Paragraph.TextIndentProperty);
            row.ClearValue(Block.PaddingProperty);
            return true;
        }

        if (marked && !hanging && row.Background is null)
        {
            row.Padding = Looks.ListIndent;
            row.TextIndent = -Looks.ListIndent.Left;
            return true;
        }

        return false;
    }

    /// <summary>La prima voce del gruppo di voci numerate consecutive a cui appartiene la riga.</summary>
    public static Paragraph? NumberedHead(Paragraph? row)
    {
        if (!Looks.IsNumbered(row))
        {
            return null;
        }

        Paragraph head = row!;

        while (head.PreviousBlock is Paragraph before && Looks.IsNumbered(before))
        {
            head = before;
        }

        return head;
    }

    /// <summary>
    /// Un elenco numerato conta da 1, sempre: tolta una voce, aggiunta una in mezzo, le altre
    /// prendono il numero giusto. Torna le righe a cui ha cambiato il numero, con di quanto si
    /// e' allungato o accorciato il segno (per rimettere il cursore dov'era).
    /// </summary>
    public static List<(Paragraph Row, int Grew)> Renumber(Paragraph head)
    {
        List<(Paragraph, int)> changed = [];
        int number = 1;

        for (Paragraph? row = head; Looks.IsNumbered(row); row = row!.NextBlock as Paragraph)
        {
            if (Looks.Counter(row!) != number && row!.Inlines.FirstInline is Run sign)
            {
                string mark = Looks.Counted(number);
                changed.Add((row, mark.Length - sign.Text.Length));
                sign.Text = mark;
            }

            number++;
        }

        return changed;
    }

    /// <summary>Un blocco di codice: una riga sola nel documento, le righe del codice separate da a capo.</summary>
    public static Paragraph CodeBlock(IEnumerable<string> lines)
    {
        Paragraph block = Doc.Row();
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

    public static IEnumerable<string> CodeLines(Paragraph block) =>
        Doc.TextOf(block).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static void Mark(Paragraph row, string mark)
    {
        Run sign = new(mark)
        {
            Foreground = Palette.Tertiary,
            FontFamily = new FontFamily(Doc.MonoFonts),
            Cursor = Cursors.Hand
        };

        if (row.Inlines.FirstInline is { } first)
        {
            row.Inlines.InsertBefore(first, sign);
        }
        else
        {
            row.Inlines.Add(sign);
        }

        row.Padding = Looks.ListIndent;
        row.TextIndent = -Looks.ListIndent.Left;
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
