using System.Text.RegularExpressions;

namespace DuckNote.Scan;

public static partial class TextSanitiser
{
    public const int DefaultLimit = 220;

    public static string Clean(string? text, int limit = DefaultLimit)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string stripped = ControlCharacters().Replace(text, string.Empty);
        string collapsed = Whitespace().Replace(stripped, " ").Trim();

        return collapsed.Length > limit ? string.Concat(collapsed.AsSpan(0, limit), "...") : collapsed;
    }

    [GeneratedRegex("[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
