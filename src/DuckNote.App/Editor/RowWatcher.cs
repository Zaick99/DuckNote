using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// Tiene dipinte le righe mentre si scrive: indirizzi nelle righe di testo, lingua nei blocchi
/// di codice. Si dipingono tutte le righe toccate (scritte, incollate, lasciate con Invio), non
/// solo quella del cursore. Ridipingere non e' scrivere: chi ascolta i cambiamenti del testo
/// guarda <see cref="IsPainting"/> e non conta queste modifiche come lavoro da salvare.
/// </summary>
public sealed class RowWatcher(RichTextBox editor, HostPainter hosts, CodePainter code)
{
    private readonly HashSet<Paragraph> _dirty = [];

    public bool IsPainting { get; private set; }

    /// <summary>Mentre un comando rimaneggia il documento, niente pittura a meta' lavoro.</summary>
    public bool Suspended { get; set; }

    public HostPainter Hosts => hosts;

    public Paragraph? CaretRow => editor.CaretPosition?.Paragraph;

    public bool HasWork => _dirty.Count > 0;

    public void Touch(Paragraph? row)
    {
        if (row is not null)
        {
            _dirty.Add(row);
        }
    }

    /// <summary>
    /// Le righe fra due punti del documento, estremi compresi (una riga incollata, cento righe
    /// incollate). Oltre un certo numero si segna tutto il documento: costa meno che camminare.
    /// </summary>
    public void Touch(TextPointer? from, TextPointer? to)
    {
        if (from is null || to is null)
        {
            return;
        }

        Paragraph? row = (from.Paragraph ?? from.GetInsertionPosition(LogicalDirection.Forward)?.Paragraph);
        int walked = 0;

        while (row is not null)
        {
            _dirty.Add(row);

            if (++walked > 400)
            {
                TouchAll();
                return;
            }

            TextPointer? next = row.ElementEnd.GetNextInsertionPosition(LogicalDirection.Forward);

            if (next is null || next.CompareTo(to) > 0)
            {
                break;
            }

            Paragraph? after = next.Paragraph;

            if (after is null || ReferenceEquals(after, row))
            {
                break;
            }

            row = after;
        }
    }

    public void TouchAll()
    {
        foreach (Paragraph row in Doc.Rows(editor.Document))
        {
            _dirty.Add(row);
        }
    }

    /// <summary>
    /// Dipinge le righe segnate. Nella riga del cursore l'indirizzo che si sta ancora scrivendo
    /// resta testo, a meno che la riga non si consideri finita (<paramref name="settled"/>);
    /// nelle altre righe non c'e' nulla in corso: si colora tutto.
    /// </summary>
    public void Flush(bool settled = false)
    {
        if (Suspended || _dirty.Count == 0)
        {
            return;
        }

        Paragraph[] pending = [.. _dirty];
        _dirty.Clear();

        Inside(() =>
        {
            Tidy(pending);

            foreach (Paragraph row in pending)
            {
                PaintRow(row, settled);
            }
        });
    }

    /// <summary>
    /// Prima di dipingere: le righe toccate (e le loro vicine) si rimettono in ordine. Rientro
    /// d'accordo con il segno, elenchi numerati che contano da 1. Il cursore resta sul carattere
    /// su cui stava anche se il numero davanti si allunga (9. diventa 10.).
    /// </summary>
    private void Tidy(Paragraph[] rows)
    {
        HashSet<Paragraph> heads = [];

        foreach (Paragraph row in rows)
        {
            if (row.Parent is null)
            {
                continue;
            }

            Styler.Heal(row);

            foreach (Paragraph? near in (Paragraph?[])[row, row.PreviousBlock as Paragraph, row.NextBlock as Paragraph])
            {
                if (Styler.NumberedHead(near) is { } head)
                {
                    heads.Add(head);
                }
            }
        }

        if (heads.Count == 0)
        {
            return;
        }

        Paragraph? here = CaretRow;
        int caret = Doc.OffsetOf(here, editor.CaretPosition);

        foreach (Paragraph head in heads)
        {
            foreach ((Paragraph row, int grew) in Styler.Renumber(head))
            {
                if (ReferenceEquals(row, here) && caret >= 0 && Doc.PointAt(row, Math.Max(0, caret + grew)) is { } back)
                {
                    editor.CaretPosition = back;
                }
            }
        }
    }

    /// <summary>La riga del cursore si considera finita: anche l'indirizzo appena scritto diventa un host.</summary>
    public void Settle()
    {
        Touch(CaretRow);
        Flush(settled: true);
    }

    public void Paint(Paragraph? row, bool settled = false)
    {
        if (row?.Parent is null || Suspended)
        {
            return;
        }

        Inside(() => PaintRow(row, settled));
    }

    public void PaintAll(FlowDocument? document = null)
    {
        FlowDocument target = document ?? editor.Document;

        void All()
        {
            foreach (Paragraph row in Doc.Rows(target))
            {
                PaintRow(row, settled: true);
            }
        }

        if (ReferenceEquals(target, editor.Document))
        {
            Inside(All);
        }
        else
        {
            Run(All);
        }
    }

    private void PaintRow(Paragraph row, bool settled)
    {
        if (row.Parent is null)
        {
            return;
        }

        if (Looks.Is(row, Look.Code))
        {
            code.Paint(row);
            return;
        }

        int caret = Doc.OffsetOf(row, editor.CaretPosition);
        hosts.Paint(row, settled ? -1 : caret);

        // il cursore resta alla stessa distanza dall'inizio della riga
        if (caret >= 0 && Doc.OffsetOf(row, editor.CaretPosition) != caret && Doc.PointAt(row, caret) is { } back)
        {
            editor.CaretPosition = back;
        }
    }

    /// <summary>
    /// La pittura nel documento aperto sta dentro un blocco di modifica: per l'editor diventa
    /// un passo solo della cronologia. Fuori da un blocco, toccare il documento a mano
    /// svuoterebbe la cronologia di Annulla a ogni indirizzo colorato.
    /// </summary>
    private void Inside(Action paint) =>
        Run(() =>
        {
            editor.BeginChange();

            try
            {
                paint();
            }
            finally
            {
                editor.EndChange();
            }
        });

    private void Run(Action paint)
    {
        bool was = IsPainting;
        IsPainting = true;

        try
        {
            paint();
        }
        catch (InvalidOperationException)
        {
            // un documento rimaneggiato a meta' si ridipinge al prossimo giro
        }
        finally
        {
            IsPainting = was;
        }
    }
}
