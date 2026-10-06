using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// Le tabelle della nota sono Markdown, non oggetti: righe di testo separate da
/// barre verticali. Aggiungere una colonna significa riscrivere ogni riga del
/// blocco, e riconoscere dove quel blocco comincia e finisce.
/// </summary>
public sealed class TableCommands(RichTextBox editor, LiveFormatter formatter)
{
    private const string Separator = "---";

    public event Action<string>? Refused;

    /// <summary>Una tabella nuova, due colonne e una riga vuota da riempire.</summary>
    public void Insert()
    {
        Paragraph? where = formatter.CaretParagraph;
        if (where is null)
        {
            return;
        }

        string[] skeleton =
        [
            "| Colonna | Colonna |",
            $"| {Separator} | {Separator} |",
            "|  |  |"
        ];

        formatter.Suspended = true;
        try
        {
            Paragraph after = where;
            bool empty = LiveFormatter.TextOf(where).Trim().Length == 0;

            foreach (string line in skeleton)
            {
                if (empty)
                {
                    new TextRange(after.ContentStart, after.ContentEnd).Text = line;
                    empty = false;
                    continue;
                }

                Paragraph added = new(new Run(line));
                editor.Document.Blocks.InsertAfter(after, added);
                after = added;
            }

            editor.CaretPosition = after.ContentEnd;
        }
        finally
        {
            formatter.Suspended = false;
        }

        formatter.FormatAll();
    }

    public void AddRow() => ChangeRows(add: true);

    public void RemoveRow() => ChangeRows(add: false);

    public void AddColumn() => ChangeColumns(add: true);

    public void RemoveColumn() => ChangeColumns(add: false);

    // --- righe ------------------------------------------------------------

    private void ChangeRows(bool add)
    {
        if (Block() is not { Count: > 0 } rows)
        {
            return;
        }

        Paragraph? here = formatter.CaretParagraph;
        if (here is null || !rows.Contains(here))
        {
            Refused?.Invoke("Mettiti dentro una tabella.");
            return;
        }

        if (add)
        {
            int columns = Cells(LiveFormatter.TextOf(rows[0])).Length;
            string blank = "|" + string.Concat(Enumerable.Repeat("  |", Math.Max(1, columns)));

            formatter.Suspended = true;
            try
            {
                editor.Document.Blocks.InsertAfter(here, new Paragraph(new Run(blank)));
            }
            finally
            {
                formatter.Suspended = false;
            }

            formatter.FormatAll();
            return;
        }

        // L'intestazione e la riga dei trattini tengono in piedi la tabella:
        // toglierle la smonterebbe, lasciando righe che non sono piu' niente.
        if (rows.IndexOf(here) < 2)
        {
            Refused?.Invoke("L'intestazione non si toglie: elimina la tabella.");
            return;
        }

        formatter.Suspended = true;
        try
        {
            editor.Document.Blocks.Remove(here);
        }
        finally
        {
            formatter.Suspended = false;
        }

        formatter.FormatAll();
    }

    // --- colonne ----------------------------------------------------------

    private void ChangeColumns(bool add)
    {
        if (Block() is not { Count: > 0 } rows)
        {
            Refused?.Invoke("Mettiti dentro una tabella.");
            return;
        }

        if (!add && Cells(LiveFormatter.TextOf(rows[0])).Length <= 1)
        {
            Refused?.Invoke("Resta una colonna sola.");
            return;
        }

        formatter.Suspended = true;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                string[] cells = Cells(LiveFormatter.TextOf(rows[i]));
                bool ruler = i == 1;

                string[] changed = add
                    ? [.. cells, ruler ? $" {Separator} " : "  "]
                    : cells[..^1];

                new TextRange(rows[i].ContentStart, rows[i].ContentEnd).Text =
                    "|" + string.Join("|", changed) + "|";
            }
        }
        finally
        {
            formatter.Suspended = false;
        }

        formatter.FormatAll();
    }

    // --- dove comincia e dove finisce --------------------------------------

    /// <summary>
    /// Le righe della tabella attorno al cursore. Una tabella Markdown e' una
    /// sequenza ininterrotta di righe che cominciano per barra: si risale e si
    /// scende finche' lo sono.
    /// </summary>
    private List<Paragraph>? Block()
    {
        if (formatter.CaretParagraph is not { } here || !IsRow(here))
        {
            return null;
        }

        List<Paragraph> rows = [here];

        for (Block? b = here.PreviousBlock; b is Paragraph p && IsRow(p); b = b.PreviousBlock)
        {
            rows.Insert(0, p);
        }

        for (Block? b = here.NextBlock; b is Paragraph p && IsRow(p); b = b.NextBlock)
        {
            rows.Add(p);
        }

        return rows;
    }

    private static bool IsRow(Paragraph paragraph) =>
        LiveFormatter.TextOf(paragraph).TrimStart().StartsWith('|');

    /// <summary>Il contenuto fra le barre, senza le due vuote agli estremi.</summary>
    private static string[] Cells(string line)
    {
        string trimmed = line.Trim();
        string inner = trimmed.Trim('|');
        return inner.Length == 0 ? [string.Empty] : inner.Split('|');
    }
}
