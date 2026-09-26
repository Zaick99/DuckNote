using System.Text.RegularExpressions;

namespace DuckNote.App.Editor;

public readonly record struct HostToken(int Start, int Length, string Value);

public static partial class HostTokens
{
    private const string Octet = @"(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)";

    private static readonly Regex Ipv4 = new(
        $@"(?<![\w.])(?:{Octet}\.){{3}}{Octet}(?:/\d{{1,2}})?(?!\w)(?!\.\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ipv6 = new(
        "(?<![0-9A-Fa-f:.])(?:" +
        "(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}" +
        "|(?:[0-9A-Fa-f]{1,4}:){1,7}:" +
        "|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}" +
        "|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}" +
        "|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}" +
        "|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}" +
        "|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}" +
        "|[0-9A-Fa-f]{1,4}:(?::[0-9A-Fa-f]{1,4}){1,6}" +
        "|:(?::[0-9A-Fa-f]{1,4}){1,7}" +
        $"|(?:[0-9A-Fa-f]{{1,4}}:){{1,6}}:(?:{Octet}\\.){{3}}{Octet}" +
        $"|::(?:[Ff]{{4}}(?::0{{1,4}})?:)?(?:{Octet}\\.){{3}}{Octet}" +
        @")(?:%[A-Za-z0-9_.-]+)?(?![0-9A-Fa-f:])(?!\.\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Fqdn = new(
        @"(?<![\w.@-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?![\w-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NotTopLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        "md","txt","rtf","exe","dll","msi","png","jpg","jpeg","gif","svg","webp","ico","bmp","tiff",
        "css","js","jsx","ts","tsx","py","rb","go","rs","java","cs","cpp","hpp","sh","bash","zsh",
        "bat","cmd","xml","json","yaml","yml","toml","ini","cfg","conf","log","lock","env",
        "bak","tmp","swp","old","orig","csv","tsv","pdf","doc","docx","xls","xlsx","ppt","pptx",
        "zip","tar","gz","bz","xz","rar","iso","img","bin","dat","db","sqlite","sql","html","htm",
        "xaml","razor","vue","svelte","mp","wav","flac","ogg","ttf","otf","woff","eot"
    };

    public static bool IsDomain(string name)
    {
        if (name.Length > 253)
        {
            return false;
        }

        string suffix = name[(name.LastIndexOf('.') + 1)..];
        if (NotTopLevel.Contains(suffix))
        {
            return false;
        }

        return suffix.Equals(suffix.ToLowerInvariant(), StringComparison.Ordinal)
            || suffix.Equals(suffix.ToUpperInvariant(), StringComparison.Ordinal);
    }

    public static IReadOnlyList<HostToken> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<HostToken> found = [];
        List<(int Start, int End)> taken = [];

        foreach (Regex pattern in (Regex[])[Ipv6, Ipv4, Fqdn])
        {
            bool isDomain = ReferenceEquals(pattern, Fqdn);

            foreach (Match match in pattern.Matches(text))
            {
                if (isDomain && !IsDomain(match.Value))
                {
                    continue;
                }

                int end = match.Index + match.Length;
                if (taken.Exists(span => match.Index < span.End && end > span.Start))
                {
                    continue;
                }

                taken.Add((match.Index, end));
                found.Add(new HostToken(match.Index, match.Length, match.Value));
            }
        }

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }
}
