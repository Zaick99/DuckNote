namespace DuckNote.App.Editor;

public static class WebAddress
{
    private static readonly string[] Allowed = ["http", "https", "mailto"];

    public static string Tidy(string written)
    {
        string trimmed = written.Trim();

        if (trimmed.Length == 0 || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed.Contains('@', StringComparison.Ordinal) && !trimmed.Contains('/', StringComparison.Ordinal)
            ? "mailto:" + trimmed
            : "https://" + trimmed;
    }

    public static Uri? Parse(string written) =>
        Uri.TryCreate(Tidy(written), UriKind.Absolute, out Uri? address) && Safe(address)
            ? address
            : null;

    public static bool Safe(Uri address) =>
        Allowed.Contains(address.Scheme, StringComparer.OrdinalIgnoreCase);
}
