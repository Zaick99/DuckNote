using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class LiveFormatter(RichTextBox editor, MarkdownRenderer renderer)
{
    private readonly HashSet<Paragraph> _dirty = [];

    public bool IsFormatting { get; private set; }

    public bool Suspended { get; set; }

    public void MarkDirty(Paragraph? paragraph)
    {
        if (paragraph is not null)
        {
            _dirty.Add(paragraph);
        }
    }

    public Paragraph? CaretParagraph => editor.CaretPosition?.Paragraph;

    public void Flush()
    {
        if (Suspended || !renderer.LiveFormatting)
        {
            _dirty.Clear();
            return;
        }

        if (!editor.Selection.IsEmpty)
        {
            return;
        }

        Paragraph[] pending = [.. _dirty];
        _dirty.Clear();

        foreach (Paragraph paragraph in pending)
        {
            Format(paragraph);
        }
    }

    public void Format(Paragraph? paragraph)
    {
        if (paragraph is null || Suspended || !renderer.LiveFormatting || paragraph.Parent is null)
        {
            return;
        }

        if (!editor.Selection.IsEmpty)
        {
            return;
        }

        string text = TextOf(paragraph);

        if (paragraph.Tag is string drawn && drawn == text)
        {
            return;
        }

        int caret = CaretOffsetIn(paragraph);
        IsFormatting = true;
        try
        {
            editor.BeginChange();
            try
            {
                paragraph.Inlines.Clear();
                renderer.Render(paragraph, text);
                paragraph.Tag = TextOf(paragraph);
            }
            finally
            {
                editor.EndChange();
            }

            if (caret >= 0)
            {
                RestoreCaret(paragraph, caret);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            IsFormatting = false;
        }
    }

    public void FormatAll()
    {
        if (editor.Document is null)
        {
            return;
        }

        IsFormatting = true;
        try
        {
            foreach (Paragraph paragraph in AllParagraphs())
            {
                string text = TextOf(paragraph);
                editor.BeginChange();
                try
                {
                    paragraph.Inlines.Clear();
                    renderer.Render(paragraph, text);
                    paragraph.Tag = TextOf(paragraph);
                }
                finally
                {
                    editor.EndChange();
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            IsFormatting = false;
        }
    }

    public IEnumerable<Paragraph> AllParagraphs() => Walk(editor.Document?.Blocks);

    private static IEnumerable<Paragraph> Walk(IEnumerable<Block>? blocks)
    {
        if (blocks is null)
        {
            yield break;
        }

        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;

                case List list:
                    foreach (ListItem item in list.ListItems)
                    {
                        foreach (Paragraph nested in Walk(item.Blocks))
                        {
                            yield return nested;
                        }
                    }
                    break;

                case Table table:
                    foreach (TableRowGroup group in table.RowGroups)
                    {
                        foreach (TableRow row in group.Rows)
                        {
                            foreach (TableCell cell in row.Cells)
                            {
                                foreach (Paragraph nested in Walk(cell.Blocks))
                                {
                                    yield return nested;
                                }
                            }
                        }
                    }
                    break;

                case Section section:
                    foreach (Paragraph nested in Walk(section.Blocks))
                    {
                        yield return nested;
                    }
                    break;

                default:
                    break;
            }
        }
    }

    public static string TextOf(Paragraph paragraph)
    {
        try
        {
            return new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private int CaretOffsetIn(Paragraph paragraph)
    {
        TextPointer? caret = editor.CaretPosition;
        if (caret is null || !ReferenceEquals(caret.Paragraph, paragraph))
        {
            return -1;
        }

        try
        {
            return new TextRange(paragraph.ContentStart, caret).Text.Length;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private void RestoreCaret(Paragraph paragraph, int offset)
    {
        TextPointer? position = paragraph.ContentStart;
        int remaining = offset;

        while (position is not null && remaining > 0 && position.CompareTo(paragraph.ContentEnd) < 0)
        {
            if (position.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                position = position.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int step = Math.Min(position.GetTextInRun(LogicalDirection.Forward).Length, remaining);
            position = position.GetPositionAtOffset(step);
            remaining -= step;
        }

        editor.CaretPosition = position ?? paragraph.ContentEnd;
    }
}
