using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class NotePage
{
    public const string Untitled = "Senza titolo";

    public required string Id { get; init; }

    public required FlowDocument Document { get; init; }

    public string Name { get; set; } = string.Empty;

    public string Title => Name.Length > 0 ? Name : Guessed();

    public string Text => string.Join('\n', LiveFormatter.RowsOf(Document).Select(Line));

    public static string FreshId() => Guid.NewGuid().ToString("n")[..12];

    public static bool IsId(string id) =>
        id.Length is > 0 and <= 32 && id.All(char.IsAsciiLetterOrDigit);

    public static string Clean(string name) =>
        string.Concat(name.Where(letter => !char.IsControl(letter))).Trim();

    private string Guessed()
    {
        Paragraph[] rows = LiveFormatter.RowsOf(Document);

        foreach (Paragraph row in rows)
        {
            if (Looks.Is(row, Look.Heading1) && Line(row).Length > 0)
            {
                return Line(row);
            }
        }

        foreach (Paragraph row in rows)
        {
            if (Line(row).Length > 0)
            {
                return Line(row);
            }
        }

        return Untitled;
    }

    private static string Line(Paragraph row) => LiveFormatter.TextOf(row).Trim('\r', '\n', ' ');
}
