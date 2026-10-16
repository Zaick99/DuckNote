using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class CodeBlocks(RichTextBox editor, Dresser renderer)
{
    public Paragraph? Fold(IReadOnlyList<Paragraph> rows)
    {
        if (rows.Count == 0 || Siblings(rows[0]) is not { } where)
        {
            return null;
        }

        Paragraph block = renderer.CodeBlock(rows.Select(Line));
        where.InsertBefore(rows[0], block);

        foreach (Paragraph row in rows)
        {
            where.Remove(row);
        }

        return block;
    }

    public Paragraph? Unfold(Paragraph block)
    {
        if (Siblings(block) is not { } where)
        {
            return null;
        }

        Paragraph? first = null;
        Paragraph after = block;

        foreach (string line in Lines(block))
        {
            Paragraph row = NoteDocument.Row();
            row.Inlines.Add(new Run(line));
            where.InsertAfter(after, row);
            after = row;
            first ??= row;
        }

        where.Remove(block);

        return first;
    }

    public static IEnumerable<string> Lines(Paragraph block) =>
        LiveFormatter.TextOf(block).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// L'invio dentro un blocco di codice aggiunge una riga al blocco. Su una
    /// riga vuota in fondo invece esce: e' il modo in cui si chiude un blocco
    /// in qualunque editor, e senza una via d'uscita dal blocco non si torna
    /// piu' a scrivere testo normale. Shift+invio resta sempre dentro.
    /// </summary>
    public bool Break()
    {
        if (editor.CaretPosition?.Paragraph is not { } block || !Looks.Is(block, Look.Code))
        {
            return false;
        }

        if (Closing(block) && Leave(block))
        {
            return true;
        }

        editor.CaretPosition = editor.CaretPosition!.InsertLineBreak();

        return true;
    }

    /// <summary>Il cursore e' in fondo al blocco, su una riga vuota.</summary>
    private bool Closing(Paragraph block)
    {
        if (editor.CaretPosition is not { } caret
            || new TextRange(caret, block.ContentEnd).Text.Trim('\r', '\n').Length > 0)
        {
            return false;
        }

        string[] lines = [.. Lines(block)];

        return lines.Length > 0 && lines[^1].Trim().Length == 0;
    }

    private bool Leave(Paragraph block)
    {
        if (NoteDocument.Siblings(block) is not { } where)
        {
            return false;
        }

        Trim(block);

        Paragraph after = NoteDocument.Row();
        where.InsertAfter(block, after);

        // Un blocco svuotato non resta a fare da cornice vuota.
        if (LiveFormatter.TextOf(block).Trim().Length == 0)
        {
            where.Remove(block);
        }

        editor.CaretPosition = after.ContentStart;

        return true;
    }

    /// <summary>Via la riga vuota in fondo: era solo il modo di chiedere di uscire.</summary>
    private static void Trim(Paragraph block)
    {
        while (block.Inlines.LastInline is Run { Text.Length: 0 } empty)
        {
            block.Inlines.Remove(empty);
        }

        if (block.Inlines.LastInline is LineBreak tail)
        {
            block.Inlines.Remove(tail);
        }
    }

    private static string Line(Paragraph row) =>
        LiveFormatter.TextOf(row).Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static BlockCollection? Siblings(Paragraph row) => NoteDocument.Siblings(row);
}
