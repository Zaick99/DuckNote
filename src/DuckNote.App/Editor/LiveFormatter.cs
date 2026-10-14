using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class LiveFormatter(RichTextBox editor, HostPaint hosts)
{
    private readonly HashSet<Paragraph> _dirty = [];

    public bool IsFormatting { get; private set; }

    public bool Suspended { get; set; }

    public Paragraph? CaretParagraph => editor.CaretPosition?.Paragraph;

    public void MarkDirty(Paragraph? paragraph)
    {
        if (paragraph is not null)
        {
            _dirty.Add(paragraph);
        }
    }

    public void Flush()
    {
        if (Suspended)
        {
            _dirty.Clear();
            return;
        }

        Paragraph[] pending = [.. _dirty];
        _dirty.Clear();

        foreach (Paragraph row in pending)
        {
            Format(row);
        }
    }

    public void Format(Paragraph? row)
    {
        if (row is null || Suspended || row.Parent is null)
        {
            return;
        }

        IsFormatting = true;
        try
        {
            int caret = OffsetOf(row, editor.CaretPosition);

            hosts.Paint(row);

            Land(row, caret);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            IsFormatting = false;
        }
    }

    private void Land(Paragraph row, int caret)
    {
        if (caret < 0 || OffsetOf(row, editor.CaretPosition) == caret)
        {
            return;
        }

        if (PointAt(row, caret) is { } back)
        {
            editor.CaretPosition = back;
        }
    }

    public void PaintAll()
    {
        IsFormatting = true;
        try
        {
            foreach (Paragraph row in Rows())
            {
                hosts.Paint(row);
            }
        }
        finally
        {
            IsFormatting = false;
        }
    }

    public void RecolourAll()
    {
        foreach (Paragraph row in Rows())
        {
            hosts.Recolour(row);
        }
    }

    public Paragraph[] Rows() => RowsOf(editor.Document);

    public static Paragraph[] RowsOf(FlowDocument? document) =>
        [.. NoteDocument.Deep(document?.Blocks).OfType<Paragraph>()];

    public static int OffsetOf(Paragraph? row, TextPointer? point)
    {
        if (row is null || point is null || !ReferenceEquals(point.Paragraph, row))
        {
            return -1;
        }

        int seen = 0;

        for (TextPointer? at = row.ContentStart; at is not null && at.CompareTo(row.ContentEnd) < 0;)
        {
            if (at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                at = at.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int length = at.GetTextRunLength(LogicalDirection.Forward);
            TextPointer? next = at.GetPositionAtOffset(length);

            if (next is null || next.CompareTo(point) > 0)
            {
                return seen + Math.Max(0, at.GetOffsetToPosition(point));
            }

            seen += length;
            at = next;
        }

        return seen;
    }

    public static TextPointer? PointAt(Paragraph row, int offset)
    {
        TextPointer? at = row.ContentStart;
        int left = offset;

        while (at is not null && left > 0 && at.CompareTo(row.ContentEnd) < 0)
        {
            if (at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                at = at.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int step = Math.Min(at.GetTextRunLength(LogicalDirection.Forward), left);
            at = at.GetPositionAtOffset(step);
            left -= step;
        }

        return left == 0 ? at : null;
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
}
