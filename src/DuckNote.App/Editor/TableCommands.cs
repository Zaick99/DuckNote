using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// Tabelle vere: intestazione con il suo sfondo, bordi di un pixel, celle che si attraversano
/// col tab. Righe di barre verticali scritte a mano (le tabelle di una volta) si recuperano:
/// cursore in quel blocco, Inserisci tabella, e diventano una tabella.
/// </summary>
public sealed class TableCommands(RichTextBox editor, TextCommands commands)
{
    private const string Placeholder = "Colonna";

    private static readonly Thickness CellBorder = new(0, 0, 1, 1);
    private static readonly Thickness TableEdge = new(1, 1, 0, 0);
    private static readonly Thickness CellPadding = new(8, 4, 8, 4);

    public event Action<string>? Refused;

    private sealed record Spot(Table Table, TableRowGroup Group, TableRow Row, TableCell Cell);

    public void Insert()
    {
        if (editor.CaretPosition?.Paragraph is not { } here || Doc.Siblings(here) is not { } where)
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

        commands.Change(() =>
        {
            Table table = written is null ? Fresh() : Converted(written);
            where.InsertBefore(written?[0] ?? here, table);

            foreach (Paragraph row in written ?? [])
            {
                where.Remove(row);
            }

            if (written is null && Doc.LineOf(here).Trim().Length == 0)
            {
                where.Remove(here);
            }

            if (table.NextBlock is null)
            {
                where.InsertAfter(table, Doc.Row());
            }

            landing = Opening(table);
        });

        Land(landing);
    }

    public void AddRow()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Metti il cursore dentro una tabella.");
            return;
        }

        TableRow fresh = Row(Enumerable.Repeat(string.Empty, spot.Row.Cells.Count), header: false);
        commands.Change(() => spot.Group.Rows.Insert(spot.Group.Rows.IndexOf(spot.Row) + 1, fresh));
        Land(Opening(fresh));
    }

    public void RemoveRow()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Metti il cursore dentro una tabella.");
            return;
        }

        int at = spot.Group.Rows.IndexOf(spot.Row);

        if (at == 0)
        {
            Refused?.Invoke("L'intestazione non si toglie: per togliere tutto, seleziona la tabella e cancellala.");
            return;
        }

        TableRow previous = spot.Group.Rows[at - 1];
        commands.Change(() => spot.Group.Rows.Remove(spot.Row));
        Land(Opening(previous));
    }

    public void AddColumn()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Metti il cursore dentro una tabella.");
            return;
        }

        int at = spot.Row.Cells.IndexOf(spot.Cell) + 1;
        TableRow header = spot.Group.Rows[0];

        commands.Change(() =>
        {
            spot.Table.Columns.Add(Column());

            foreach (TableRow row in spot.Group.Rows)
            {
                bool heading = ReferenceEquals(row, header);
                row.Cells.Insert(Math.Min(at, row.Cells.Count), Cell(heading ? Placeholder : string.Empty, heading));
            }
        });
    }

    public void RemoveColumn()
    {
        if (Inside() is not { } spot)
        {
            Refused?.Invoke("Metti il cursore dentro una tabella.");
            return;
        }

        if (spot.Row.Cells.Count <= 1)
        {
            Refused?.Invoke("Resta una colonna sola.");
            return;
        }

        int at = spot.Row.Cells.IndexOf(spot.Cell);

        commands.Change(() =>
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

    /// <summary>Una tabella costruita da righe di testo (per mandare in nota i risultati di una scansione).</summary>
    public Table Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        Table table = Shell(header.Count);
        table.RowGroups[0].Rows.Add(Row(header, header: true));

        foreach (IReadOnlyList<string> row in rows)
        {
            table.RowGroups[0].Rows.Add(Row(row, header: false));
        }

        return table;
    }

    private Table Fresh()
    {
        Table table = Shell(2);
        table.RowGroups[0].Rows.Add(Row([Placeholder, Placeholder], header: true));
        table.RowGroups[0].Rows.Add(Row([string.Empty, string.Empty], header: false));

        return table;
    }

    private Table Converted(List<Paragraph> written)
    {
        string[][] rows = [.. written.Select(line => Cells(Doc.LineOf(line))).Where(cells => !IsRuler(cells))];
        int columns = rows.Length == 0 ? 2 : rows.Max(row => row.Length);
        Table table = Shell(columns);

        for (int i = 0; i < rows.Length; i++)
        {
            table.RowGroups[0].Rows.Add(Row(rows[i].Concat(Enumerable.Repeat(string.Empty, columns - rows[i].Length)), header: i == 0));
        }

        if (rows.Length == 0)
        {
            table.RowGroups[0].Rows.Add(Row(Enumerable.Repeat(Placeholder, columns), header: true));
        }

        return table;
    }

    private static Table Shell(int columns)
    {
        Table table = new()
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 8),
            BorderBrush = Palette.TableLine,
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

    private static TableRow Row(IEnumerable<string> texts, bool header)
    {
        TableRow row = new();

        foreach (string text in texts)
        {
            row.Cells.Add(Cell(text, header));
        }

        return row;
    }

    private static TableCell Cell(string text, bool header) => new(Doc.Row(text))
    {
        Padding = CellPadding,
        BorderBrush = Palette.TableLine,
        BorderThickness = CellBorder,
        Background = header ? Palette.TableHead : null,
        FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
    };

    private Spot? Inside() =>
        editor.CaretPosition?.Paragraph is { Parent: TableCell { Parent: TableRow { Parent: TableRowGroup { Parent: Table table } group } row } cell }
            ? new Spot(table, group, row, cell)
            : null;

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
            editor.Focus();
        }
    }

    private static List<Paragraph>? Written(Paragraph here)
    {
        if (!IsPiped(here))
        {
            return null;
        }

        List<Paragraph> rows = [here];

        for (Block? b = here.PreviousBlock; b is Paragraph p && IsPiped(p); b = b.PreviousBlock)
        {
            rows.Insert(0, p);
        }

        for (Block? b = here.NextBlock; b is Paragraph p && IsPiped(p); b = b.NextBlock)
        {
            rows.Add(p);
        }

        return rows;
    }

    private static bool IsPiped(Paragraph row) => Doc.LineOf(row).TrimStart().StartsWith('|');

    private static string[] Cells(string line)
    {
        string inner = line.Trim().Trim('|');

        return inner.Length == 0 ? [string.Empty] : [.. inner.Split('|').Select(cell => cell.Trim())];
    }

    private static bool IsRuler(string[] cells) =>
        cells.Length > 0 && cells.All(cell => cell.Length > 0 && cell.All(c => c is '-' or ':'));
}
