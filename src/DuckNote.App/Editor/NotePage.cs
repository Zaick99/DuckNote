using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class NotePage
{
    public const string Untitled = "Senza titolo";

    public required string Id { get; init; }

    public required FlowDocument Document { get; init; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Il titolo lo mette una persona, col tasto destro: non si indovina dal
    /// primo titolo scritto dentro. Quello e' un titolo del testo, e nella
    /// struttura compare come tale.
    /// </summary>
    public string Title => Name.Length > 0 ? Name : Untitled;

    public string Text => string.Join('\n', LiveFormatter.RowsOf(Document).Select(Line));

    public static string FreshId() => Guid.NewGuid().ToString("n")[..12];

    public static bool IsId(string id) =>
        id.Length is > 0 and <= 32 && id.All(char.IsAsciiLetterOrDigit);

    public static string Clean(string name) =>
        string.Concat(name.Where(letter => !char.IsControl(letter))).Trim();

    private static string Line(Paragraph row) => LiveFormatter.TextOf(row).Trim('\r', '\n', ' ');
}
