namespace DuckNote.Notes.Links;

/// <summary>Gli indirizzi che un collegamento puo' portare: http, https, mailto. Nient'altro.</summary>
public static class WebAddress
{
    private static readonly string[] Allowed = ["http", "https", "mailto"];

    public static string Tidy(string? written)
    {
        string trimmed = (written ?? string.Empty).Trim();

        if (trimmed.Length == 0 || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed.Contains('@') && !trimmed.Contains('/') ? "mailto:" + trimmed : "https://" + trimmed;
    }

    public static Uri? Parse(string? written) =>
        Uri.TryCreate(Tidy(written), UriKind.Absolute, out Uri? address) && Safe(address) ? address : null;

    public static bool Safe(Uri? address) =>
        address is { IsAbsoluteUri: true } && Allowed.Contains(address.Scheme, StringComparer.OrdinalIgnoreCase);
}
