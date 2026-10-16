using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DuckNote.App.Editor;

/// <summary>
/// Veste un blocco di codice con i colori della sua lingua: le parole che la
/// lingua si tiene, i testi fra virgolette, i numeri, i commenti e i nomi delle
/// funzioni. La lingua non e' scritta da nessuna parte, la si indovina.
/// </summary>
public sealed class CodePaint(EditorBrushes brushes, RichTextBox editor)
{
    /// <summary>Un pezzo di riga gia' vestito.</summary>
    private readonly record struct Bit(string Text, CodeInk Ink);

    public void Paint(Paragraph block)
    {
        string[] lines = [.. CodeBlocks.Lines(block)];
        List<List<Bit>> rows = Dressed(lines, CodeTongue.Of(lines));

        if (Same(block, rows))
        {
            return;
        }

        // Rifare i pezzi li toglie di sotto al cursore: lo si rimette per riga
        // e colonna, non per numero di caratteri -- in un blocco i fine riga
        // sono elementi, e contarli come caratteri sposta di uno a ogni riga.
        (int Line, int Column)? spot = Spot(block, editor.CaretPosition);

        Rebuild(block, rows);

        if (spot is { } back)
        {
            editor.CaretPosition = Point(block, back.Line, back.Column);
        }
    }

    private static List<List<Bit>> Dressed(string[] lines, Tongue tongue)
    {
        List<List<Bit>> rows = [];
        CodeScan.Carry carry = CodeScan.Carry.None;

        foreach (string line in lines)
        {
            List<Bit> bits = [];
            int at = 0;

            foreach (CodePiece piece in CodeScan.Line(line, tongue, ref carry))
            {
                Add(bits, line[at..piece.Start], CodeInk.Plain);
                Add(bits, line.Substring(piece.Start, piece.Length), piece.Ink);
                at = piece.Start + piece.Length;
            }

            Add(bits, line[at..], CodeInk.Plain);

            if (bits.Count == 0)
            {
                bits.Add(new Bit(string.Empty, CodeInk.Plain));
            }

            rows.Add(bits);
        }

        return rows;
    }

    /// <summary>Pezzi vicini dello stesso colore sono un pezzo solo.</summary>
    private static void Add(List<Bit> bits, string text, CodeInk ink)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (bits.Count > 0 && bits[^1].Ink == ink)
        {
            bits[^1] = bits[^1] with { Text = bits[^1].Text + text };
            return;
        }

        bits.Add(new Bit(text, ink));
    }

    /// <summary>Il blocco e' gia' vestito cosi': non si tocca niente.</summary>
    private bool Same(Paragraph block, List<List<Bit>> rows)
    {
        int row = 0;
        int bit = 0;

        foreach (Inline piece in block.Inlines)
        {
            if (piece is LineBreak)
            {
                if (row >= rows.Count || bit != rows[row].Count)
                {
                    return false;
                }

                row++;
                bit = 0;
                continue;
            }

            if (piece is not Run run || row >= rows.Count || bit >= rows[row].Count)
            {
                return false;
            }

            if (run.Text != rows[row][bit].Text || !ReferenceEquals(run.Foreground, Ink(rows[row][bit].Ink)))
            {
                return false;
            }

            bit++;
        }

        return row == rows.Count - 1 && bit == rows[row].Count;
    }

    private void Rebuild(Paragraph block, List<List<Bit>> rows)
    {
        block.Inlines.Clear();

        for (int row = 0; row < rows.Count; row++)
        {
            if (row > 0)
            {
                block.Inlines.Add(new LineBreak());
            }

            foreach (Bit bit in rows[row])
            {
                block.Inlines.Add(new Run(bit.Text) { Foreground = Ink(bit.Ink) });
            }
        }
    }

    private Brush Ink(CodeInk ink) => ink switch
    {
        CodeInk.Keyword => brushes.CodeWord,
        CodeInk.Quote => brushes.CodeQuote,
        CodeInk.Number => brushes.CodeNumber,
        CodeInk.Comment => brushes.CodeRemark,
        CodeInk.Call => brushes.CodeCall,
        _ => brushes.CodeForeground
    };

    /// <summary>A che riga e a che colonna sta il cursore dentro il blocco.</summary>
    private static (int Line, int Column)? Spot(Paragraph block, TextPointer? caret)
    {
        if (caret is null || !ReferenceEquals(caret.Paragraph, block))
        {
            return null;
        }

        int line = 0;
        int column = 0;

        for (TextPointer? at = block.ContentStart; at is not null && at.CompareTo(caret) < 0;)
        {
            if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int length = at.GetTextRunLength(LogicalDirection.Forward);
                TextPointer? next = at.GetPositionAtOffset(length);

                if (next is null || next.CompareTo(caret) > 0)
                {
                    return (line, column + at.GetOffsetToPosition(caret));
                }

                column += length;
                at = next;
                continue;
            }

            // Solo all'inizio dell'elemento: un fine riga si attraversa in due
            // passi, e contarlo a tutti e due vorrebbe dire saltare una riga.
            if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
                && at.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
            {
                line++;
                column = 0;
            }

            at = at.GetNextContextPosition(LogicalDirection.Forward);
        }

        return (line, column);
    }

    /// <summary>Il posto, nei pezzi rifatti, di quella riga e quella colonna.</summary>
    private static TextPointer Point(Paragraph block, int line, int column)
    {
        int row = 0;
        int seen = 0;
        TextPointer where = block.ContentStart;

        foreach (Inline piece in block.Inlines)
        {
            if (piece is LineBreak)
            {
                if (row == line)
                {
                    return where;
                }

                row++;
                seen = 0;
                where = piece.ContentEnd;
                continue;
            }

            if (piece is not Run run || row != line)
            {
                continue;
            }

            if (seen + run.Text.Length >= column)
            {
                return run.ContentStart.GetPositionAtOffset(column - seen) ?? run.ContentEnd;
            }

            seen += run.Text.Length;
            where = run.ContentEnd;
        }

        return where;
    }
}
