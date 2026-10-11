using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class Finder(RichTextBox editor)
{
    private readonly List<TextRange> _hits = [];

    private string _needle = string.Empty;
    private bool _exact;
    private int _at = -1;

    public int Count => _hits.Count;

    public int Current => _at < 0 ? 0 : _at + 1;

    public string Needle => _needle;

    public void Look(string needle, bool exact)
    {
        _needle = needle;
        _exact = exact;
        _at = -1;
        _hits.Clear();

        if (needle.Length == 0)
        {
            return;
        }

        foreach (Paragraph row in LiveFormatter.RowsOf(editor.Document))
        {
            Gather(row);
        }
    }

    public TextRange? Step(int direction, TextPointer? from)
    {
        if (_hits.Count == 0)
        {
            return null;
        }

        if (_at < 0)
        {
            _at = direction >= 0 ? Nearest(from) : Previous(from);
            return _hits[_at];
        }

        _at = ((_at + direction) % _hits.Count + _hits.Count) % _hits.Count;

        return _hits[_at];
    }

    public TextRange? Here() => _at >= 0 && _at < _hits.Count ? _hits[_at] : null;

    public bool Swap(string with)
    {
        if (Here() is not { } hit)
        {
            return false;
        }

        hit.Text = with;
        Look(_needle, _exact);

        return true;
    }

    public int SwapAll(string with)
    {
        TextRange[] all = [.. _hits];

        for (int i = all.Length - 1; i >= 0; i--)
        {
            all[i].Text = with;
        }

        Look(_needle, _exact);

        return all.Length;
    }

    private int Nearest(TextPointer? from)
    {
        if (from is null)
        {
            return 0;
        }

        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Start.CompareTo(from) >= 0)
            {
                return i;
            }
        }

        return 0;
    }

    private int Previous(TextPointer? from)
    {
        if (from is null)
        {
            return _hits.Count - 1;
        }

        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].End.CompareTo(from) <= 0)
            {
                return i;
            }
        }

        return _hits.Count - 1;
    }

    private void Gather(Paragraph row)
    {
        List<Chunk> chunks = [];
        System.Text.StringBuilder line = new();

        for (TextPointer? point = row.ContentStart;
             point is not null && point.CompareTo(row.ContentEnd) < 0;
             point = Advance(point))
        {
            if (point.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                continue;
            }

            int length = point.GetTextRunLength(LogicalDirection.Forward);
            if (length == 0)
            {
                continue;
            }

            chunks.Add(new Chunk(point, line.Length, length));
            line.Append(point.GetTextInRun(LogicalDirection.Forward));
        }

        string text = line.ToString();
        StringComparison how = _exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (int at = text.IndexOf(_needle, how); at >= 0; at = text.IndexOf(_needle, at + 1, how))
        {
            if (Spot(chunks, at) is { } start && Spot(chunks, at + _needle.Length) is { } end)
            {
                _hits.Add(new TextRange(start, end));
            }
        }
    }

    private static TextPointer? Advance(TextPointer point) =>
        point.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text
            ? point.GetPositionAtOffset(point.GetTextRunLength(LogicalDirection.Forward))
            : point.GetNextContextPosition(LogicalDirection.Forward);

    private static TextPointer? Spot(List<Chunk> chunks, int at)
    {
        foreach (Chunk chunk in chunks)
        {
            if (at >= chunk.At && at <= chunk.At + chunk.Length)
            {
                return chunk.Start.GetPositionAtOffset(at - chunk.At);
            }
        }

        return null;
    }

    private sealed record Chunk(TextPointer Start, int At, int Length);
}
