namespace DuckNote.Notes.Hosts;

/// <summary>Gli host messi da parte: una lista scritta come testo, separata da virgole.</summary>
public static class HostList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    public static IReadOnlyList<string> Parse(string? written) =>
        [.. (written ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    public static string Write(IEnumerable<string> hosts) =>
        string.Join(',', hosts.Where(host => host.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}
