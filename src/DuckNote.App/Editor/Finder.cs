using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// Cerca nella pagina. Le occorrenze si trovano attraverso i pezzi di testo: una parola mezza
/// in grassetto resta una parola sola.
/// </summary>
public sealed class Finder(RichTextBox editor)
{
    private readonly List<TextRange> _hits = [];
    private bool _exact;
    private int _at = -1;

    public int Count => _hits.Count;

    public int Current => _at < 0 ? 0 : _at + 1;

    public string Needle { get; private set; } = string.Empty;

    public IReadOnlyList<TextRange> Hits => _hits;

    public void Look(string needle, bool exact)
    {
        Needle = needle;
        _exact = exact;
        _at = -1;
        _hits.Clear();

        if (needle.Length == 0)
        {
            return;
        }

        foreach (Paragraph row in Doc.Rows(editor.Document))
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
            _at = direction >= 0 ? Next(from) : Previous(from);
        }
        else
        {
            _at = ((_at + direction) % _hits.Count + _hits.Count) % _hits.Count;
        }

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
        Look(Needle, _exact);

        return true;
    }

    public int SwapAll(string with)
    {
        TextRange[] all = [.. _hits];

        for (int i = all.Length - 1; i >= 0; i--)
        {
            all[i].Text = with;
        }

        Look(Needle, _exact);

        return all.Length;
    }

    private int Next(TextPointer? from)
    {
        int found = from is null ? -1 : _hits.FindIndex(hit => hit.Start.CompareTo(from) >= 0);

        return Math.Max(0, found);
    }

    private int Previous(TextPointer? from)
    {
        int found = from is null ? -1 : _hits.FindLastIndex(hit => hit.End.CompareTo(from) <= 0);

        return found < 0 ? _hits.Count - 1 : found;
    }

    private void Gather(Paragraph row)
    {
        List<(TextPointer Start, int At, int Length)> chunks = [];
        StringBuilder line = new();

        for (TextPointer? point = row.ContentStart; point is not null && point.CompareTo(row.ContentEnd) < 0;)
        {
            if (point.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                point = point.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int length = point.GetTextRunLength(LogicalDirection.Forward);
            chunks.Add((point, line.Length, length));
            line.Append(point.GetTextInRun(LogicalDirection.Forward));
            point = point.GetPositionAtOffset(length);
        }

        string text = line.ToString();
        StringComparison how = _exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (int at = text.IndexOf(Needle, how); at >= 0; at = text.IndexOf(Needle, at + 1, how))
        {
            if (Spot(chunks, at) is { } start && Spot(chunks, at + Needle.Length) is { } end)
            {
                _hits.Add(new TextRange(start, end));
            }
        }
    }

    private static TextPointer? Spot(List<(TextPointer Start, int At, int Length)> chunks, int at)
    {
        foreach ((TextPointer start, int from, int length) in chunks)
        {
            if (at >= from && at <= from + length)
            {
                return start.GetPositionAtOffset(at - from);
            }
        }

        return null;
    }
}
