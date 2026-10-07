using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class TableCommands(RichTextBox editor, LiveFormatter formatter, EditorBrushes brushes)
{
    private const string Placeholder = "Colonna";

    private static readonly Thickness CellBorder = new(0, 0, 1, 1);
    private static readonly Thickness TableEdge = new(1, 1, 0, 0);
    private static readonly Thickness CellPadding = new(8, 4, 8, 4);

    public event Action<string>? Refused;

    public void Insert()
    {
        if (formatter.CaretParagraph is not { } here)
        {
            return;
        }

        if (Inside() is not null)
        {
            Refused?.Invoke("Il cursore e' dentro una tabella: escine per crearne un'altra.");
            return;
        }

        List<Paragraph>? written = Written(here);
        Paragraph? landing = null;

        Edit(() =>
        {
            Table table = written is null ? Fresh() : Converted(written);
            editor.Document.Blocks.InsertBefore(written?[0] ?? here, table);

            foreach (Paragraph row in written ?? [])
            {
                editor.Document.Blocks.Remove(row);
            }

            if (written is null && LiveFormatter.TextOf(here).Trim().Length == 0)
            {
                editor.Document.Blocks.Remove(here);
            }

            if (table.NextBlock is null)
            {
                editor.Document.Blocks.InsertAfter(table, new Paragraph { Margin = new Thickness(0) });
            }

            landing = Opening(table);
        });

        formatter.PaintAll();
        Land(landing);
    }

    public void AddRow()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Mettiti dentro una tabella.");
            return;
        }

        Paragraph? landing = null;

        Edit(() =>
        {
            TableRow fresh = Row(Enumerable.Repeat(string.Empty, spot.Row.Cells.Count), header: false);
            spot.Group.Rows.Insert(spot.Group.Rows.IndexOf(spot.Row) + 1, fresh);
            landing = Opening(fresh);
        });

        Land(landing);
    }

    public void RemoveRow()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Mettiti dentro una tabella.");
            return;
        }

        int at = spot.Group.Rows.IndexOf(spot.Row);

        if (at == 0)
        {
            Refused?.Invoke("L'intestazione non si toglie: elimina la tabella.");
            return;
        }

        TableRow previous = spot.Group.Rows[at - 1];
        Paragraph? landing = null;

        Edit(() =>
        {
            spot.Group.Rows.Remove(spot.Row);
            landing = Opening(previous);
        });

        Land(landing);
    }

    public void AddColumn()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Mettiti dentro una tabella.");
            return;
        }

        int at = spot.Row.Cells.IndexOf(spot.Cell) + 1;
        TableRow header = spot.Group.Rows[0];

        Edit(() =>
        {
            spot.Table.Columns.Add(Column());

            foreach (TableRow row in spot.Group.Rows)
            {
                bool heading = ReferenceEquals(row, header);
                row.Cells.Insert(Math.Min(at, row.Cells.Count), Cell(heading ? Placeholder : string.Empty, heading));
            }
        });

        formatter.PaintAll();
    }

    public void RemoveColumn()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Mettiti dentro una tabella.");
            return;
        }

        if (spot.Row.Cells.Count <= 1)
        {
            Refused?.Invoke("Resta una colonna sola.");
            return;
        }

        int at = spot.Row.Cells.IndexOf(spot.Cell);

        Edit(() =>
        {
            foreach (TableRow row in spot.Group.Rows)
            {
                if (at < row.Cells.Count)
                {
                    row.Cells.RemoveAt(at);
                }
            }

            spot.Table.Columns.RemoveAt(spot.Table.Columns.Count - 1);
        });
    }

    private Table Fresh()
    {
        Table table = Shell(columns: 2);

        table.RowGroups[0].Rows.Add(Row([Placeholder, Placeholder], header: true));
        table.RowGroups[0].Rows.Add(Row([string.Empty, string.Empty], header: false));

        return table;
    }

    private Table Converted(List<Paragraph> written)
    {
        string[][] rows =
        [
            .. written
                .Select(line => Cells(LiveFormatter.TextOf(line)))
                .Where(cells => !IsRuler(cells))
        ];

        int columns = rows.Length == 0 ? 2 : rows.Max(row => row.Length);
        Table table = Shell(columns);

        for (int i = 0; i < rows.Length; i++)
        {
            table.RowGroups[0].Rows.Add(Row(Fit(rows[i], columns), header: i == 0));
        }

        if (table.RowGroups[0].Rows.Count == 0)
        {
            table.RowGroups[0].Rows.Add(Row(Enumerable.Repeat(Placeholder, columns), header: true));
        }

        return table;
    }

    private Table Shell(int columns)
    {
        Table table = new()
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 8),
            BorderBrush = brushes.TableLine,
            BorderThickness = TableEdge
        };

        for (int i = 0; i < columns; i++)
        {
            table.Columns.Add(Column());
        }

        table.RowGroups.Add(new TableRowGroup());

        return table;
    }

    private static TableColumn Column() => new() { Width = new GridLength(1, GridUnitType.Star) };

    private TableRow Row(IEnumerable<string> texts, bool header)
    {
        TableRow row = new();

        foreach (string text in texts)
        {
            row.Cells.Add(Cell(text, header));
        }

        return row;
    }

    private TableCell Cell(string text, bool header)
    {
        Paragraph content = new() { Margin = new Thickness(0) };

        if (text.Length > 0)
        {
            content.Inlines.Add(new Run(text));
        }

        return new TableCell(content)
        {
            Padding = CellPadding,
            BorderBrush = brushes.TableLine,
            BorderThickness = CellBorder,
            Background = header ? brushes.TableHeader : null,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
        };
    }

    private Spot? Inside()
    {
        if (editor.CaretPosition?.Paragraph is not { Parent: TableCell cell })
        {
            return null;
        }

        if (cell.Parent is not TableRow row || row.Parent is not TableRowGroup group || group.Parent is not Table table)
        {
            return null;
        }

        return new Spot(table, group, row, cell);
    }

    private sealed record Spot(Table Table, TableRowGroup Group, TableRow Row, TableCell Cell);

    private static Paragraph? Opening(Table table)
    {
        TableRowGroup group = table.RowGroups[0];

        return group.Rows.Count > 1 ? Opening(group.Rows[1]) : group.Rows.Count > 0 ? Opening(group.Rows[0]) : null;
    }

    private static Paragraph? Opening(TableRow row) => row.Cells.FirstOrDefault()?.Blocks.FirstBlock as Paragraph;

    private void Land(Paragraph? landing)
    {
        if (landing?.Parent is not null)
        {
            editor.CaretPosition = landing.ContentStart;
        }
    }

    private void Edit(Action change)
    {
        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                change();
            }
            finally
            {
                editor.EndChange();
            }
        }
        finally
        {
            formatter.Suspended = false;
        }
    }

    private static List<Paragraph>? Written(Paragraph here)
    {
        if (!IsRow(here))
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

    private static string[] Cells(string line)
    {
        string inner = line.Trim().Trim('|');

        return inner.Length == 0 ? [string.Empty] : [.. inner.Split('|').Select(cell => cell.Trim())];
    }

    private static bool IsRuler(string[] cells) =>
        cells.Length > 0 && cells.All(cell => cell.Length > 0 && cell.All(letter => letter is '-' or ':'));

    private static IEnumerable<string> Fit(string[] cells, int columns) =>
        cells.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, columns - cells.Length)));
}
