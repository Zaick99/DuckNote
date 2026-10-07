using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class CodeBlocks(RichTextBox editor, MarkdownRenderer renderer)
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
            Paragraph row = new(new Run(line));
            where.InsertAfter(after, row);
            after = row;
            first ??= row;
        }

        where.Remove(block);

        return first;
    }

    public static IEnumerable<string> Lines(Paragraph block) =>
        LiveFormatter.TextOf(block).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public bool Break()
    {
        if (!Looks.Is(editor.CaretPosition?.Paragraph, Look.Code))
        {
            return false;
        }

        editor.CaretPosition = editor.CaretPosition!.InsertLineBreak();

        return true;
    }

    private static string Line(Paragraph row) =>
        LiveFormatter.TextOf(row).Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static BlockCollection? Siblings(Paragraph row) => row.Parent switch
    {
        FlowDocument document => document.Blocks,
        TableCell cell => cell.Blocks,
        ListItem item => item.Blocks,
        Section section => section.Blocks,
        _ => null
    };
}
