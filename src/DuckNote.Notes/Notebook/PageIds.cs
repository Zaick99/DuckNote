namespace DuckNote.Notes.Notebook;

/// <summary>Identificativi e nomi delle pagine: brevi, solo lettere e cifre, senza caratteri di controllo.</summary>
public static class PageIds
{
    public const string Untitled = "Senza titolo";

    public static string Fresh() => Guid.NewGuid().ToString("n")[..12];

    public static bool IsValid(string? id) =>
        id is { Length: > 0 and <= 32 } && id.All(char.IsAsciiLetterOrDigit);

    public static string Clean(string? name) =>
        string.Concat((name ?? string.Empty).Where(letter => !char.IsControl(letter))).Trim();

    public static string Title(string? name) => Clean(name) is { Length: > 0 } clean ? clean : Untitled;
}
