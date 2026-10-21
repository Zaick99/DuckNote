using System.Text;

namespace DuckNote.Notes.Scanning;

/// <summary>
/// Una tabella in CSV, col punto e virgola: e' il separatore che Excel in italiano apre senza
/// chiedere niente. Il BOM in testa gli dice che il file e' UTF-8 (accenti compresi).
/// </summary>
public static class CsvTable
{
    public const char Separator = ';';

    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        StringBuilder text = new();
        Line(text, header);

        foreach (IReadOnlyList<string> row in rows)
        {
            Line(text, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public static string Cell(string? value)
    {
        string text = value ?? string.Empty;

        // Una cella che comincia con = + - @ Excel la esegue come formula: un apice davanti la
        // tiene testo. Un banner di rete non deve poter diventare un comando.
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        bool quote = text.IndexOfAny([Separator, '"', '\n', '\r']) >= 0;

        return quote ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static void Line(StringBuilder text, IReadOnlyList<string> cells)
    {
        text.AppendJoin(Separator, cells.Select(Cell));
        text.Append("\r\n");
    }
}
