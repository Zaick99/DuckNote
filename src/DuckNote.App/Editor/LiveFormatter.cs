using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class LiveFormatter(RichTextBox editor, HostPaint hosts, CodePaint code)
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

    public void Flush(bool settled = false)
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
            Format(row, settled);
        }
    }

    /// <summary>
    /// La riga dove sta il cursore si considera finita: anche l'indirizzo
    /// appena scritto diventa un host.
    /// </summary>
    public void Settle()
    {
        MarkDirty(CaretParagraph);
        Flush(settled: true);
    }

    /// <summary>
    /// Ridipinge una riga. Con <paramref name="settled"/> a false l'indirizzo
    /// che il cursore sta ancora scrivendo resta testo: diventa un host quando
    /// il cursore se ne va -- uno spazio, un invio, un clic altrove -- o quando
    /// si smette di scrivere e la riga si considera finita.
    /// </summary>
    public void Format(Paragraph? row, bool settled = false)
    {
        if (row is null || Suspended || row.Parent is null)
        {
            return;
        }

        IsFormatting = true;
        try
        {
            // Dentro un blocco di codice un indirizzo e' codice, non un host:
            // li' comanda la lingua, e si tiene il cursore da se'.
            if (Looks.Is(row, Look.Code))
            {
                code.Paint(row);
                return;
            }

            int caret = OffsetOf(row, editor.CaretPosition);

            hosts.Paint(row, settled ? -1 : caret);

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

    /// <summary>
    /// Ridipingere non e' scrivere. Un esito di scansione che arriva cambia il
    /// colore degli indirizzi, e senza questa guardia per WPF e' una modifica
    /// come un'altra: faceva ripartire da zero l'orologio che aspetta che si
    /// smetta di scrivere -- con una scansione in corso non scadeva mai -- e
    /// rimetteva la nota fra quelle da salvare.
    /// </summary>
    public void RecolourAll()
    {
        IsFormatting = true;
        try
        {
            foreach (Paragraph row in Rows())
            {
                hosts.Recolour(row);
            }
        }
        finally
        {
            IsFormatting = false;
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
