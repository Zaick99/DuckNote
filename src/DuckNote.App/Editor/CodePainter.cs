using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DuckNote.Notes.Code;

namespace DuckNote.App.Editor;

/// <summary>
/// Colora un blocco di codice secondo la lingua che sembra parlare. Rifare i pezzi toglie il
/// cursore di sotto: lo si rimette per riga e colonna, perche' in un blocco gli a capo sono
/// elementi e contarli come caratteri sposterebbe di uno a ogni riga.
/// </summary>
public sealed class CodePainter(RichTextBox editor)
{
    public void Paint(Paragraph block)
    {
        string[] lines = [.. Styler.CodeLines(block)];
        IReadOnlyList<IReadOnlyList<CodeSpan>> rows = CodeLexer.Paint(lines);

        if (Same(block, rows))
        {
            return;
        }

        (int Line, int Column)? spot = Spot(block, editor.CaretPosition);

        block.Inlines.Clear();

        for (int row = 0; row < rows.Count; row++)
        {
            if (row > 0)
            {
                block.Inlines.Add(new LineBreak());
            }

            if (rows[row].Count == 0)
            {
                block.Inlines.Add(new Run(string.Empty) { Foreground = Ink(CodeInk.Plain) });
            }

            foreach (CodeSpan span in rows[row])
            {
                block.Inlines.Add(new Run(span.Text) { Foreground = Ink(span.Ink) });
            }
        }

        if (spot is { } back)
        {
            editor.CaretPosition = Point(block, back.Line, back.Column);
        }
    }

    private static Brush Ink(CodeInk ink) => ink switch
    {
        CodeInk.Keyword => Palette.Of("Purple"),
        CodeInk.Quote => Palette.Of("Green"),
        CodeInk.Number => Palette.Of("Orange"),
        CodeInk.Comment => Palette.Tertiary,
        CodeInk.Call => Palette.Of("Blue"),
        _ => Palette.CodeFore
    };

    /// <summary>Il blocco e' gia' vestito cosi': non si tocca niente (e il cursore resta dov'e').</summary>
    private static bool Same(Paragraph block, IReadOnlyList<IReadOnlyList<CodeSpan>> rows)
    {
        List<(bool Break, string Text, Brush? Ink)> wanted = [];

        for (int row = 0; row < rows.Count; row++)
        {
            if (row > 0)
            {
                wanted.Add((true, string.Empty, null));
            }

            if (rows[row].Count == 0)
            {
                wanted.Add((false, string.Empty, Ink(CodeInk.Plain)));
            }

            wanted.AddRange(rows[row].Select(span => (false, span.Text, (Brush?)Ink(span.Ink))));
        }

        Inline[] pieces = [.. block.Inlines];

        if (pieces.Length != wanted.Count)
        {
            return false;
        }

        for (int i = 0; i < pieces.Length; i++)
        {
            bool ok = wanted[i].Break
                ? pieces[i] is LineBreak
                : pieces[i] is Run run && run.Text == wanted[i].Text && ReferenceEquals(run.Foreground, wanted[i].Ink);

            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

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
            TextPointerContext context = at.GetPointerContext(LogicalDirection.Forward);

            if (context == TextPointerContext.Text)
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

            // un a capo si attraversa in due passi: contarlo solo all'ingresso
            if (context == TextPointerContext.ElementStart && at.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
            {
                line++;
                column = 0;
            }

            at = at.GetNextContextPosition(LogicalDirection.Forward);
        }

        return (line, column);
    }

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
                where = piece.ElementEnd;
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
