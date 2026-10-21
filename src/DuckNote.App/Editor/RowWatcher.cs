using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// Tiene dipinte le righe mentre si scrive: indirizzi nelle righe di testo, lingua nei blocchi
/// di codice. Ridipingere non e' scrivere: chi ascolta i cambiamenti del testo guarda
/// <see cref="IsPainting"/> e non conta queste modifiche come lavoro da salvare.
/// </summary>
public sealed class RowWatcher(RichTextBox editor, HostPainter hosts, CodePainter code)
{
    private readonly HashSet<Paragraph> _dirty = [];

    public bool IsPainting { get; private set; }

    /// <summary>Mentre un comando rimaneggia il documento, niente pittura a meta' lavoro.</summary>
    public bool Suspended { get; set; }

    public Paragraph? CaretRow => editor.CaretPosition?.Paragraph;

    public void Touch(Paragraph? row)
    {
        if (row is not null)
        {
            _dirty.Add(row);
        }
    }

    public void Flush(bool settled = false)
    {
        Paragraph[] pending = [.. _dirty];
        _dirty.Clear();

        if (Suspended)
        {
            return;
        }

        foreach (Paragraph row in pending)
        {
            Paint(row, settled);
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

        Run(() =>
        {
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
        });
    }

    public void PaintAll(FlowDocument? document = null) =>
        Run(() =>
        {
            foreach (Paragraph row in Doc.Rows(document ?? editor.Document))
            {
                if (Looks.Is(row, Look.Code))
                {
                    code.Paint(row);
                }
                else
                {
                    hosts.Paint(row);
                }
            }
        });

    public void RecolourAll(FlowDocument? document = null) =>
        Run(() =>
        {
            foreach (Paragraph row in Doc.Rows(document ?? editor.Document))
            {
                hosts.Recolour(row);
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
