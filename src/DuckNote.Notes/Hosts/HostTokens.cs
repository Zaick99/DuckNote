using System.Text.RegularExpressions;

namespace DuckNote.Notes.Hosts;

/// <summary>Un indirizzo trovato in una riga: dove comincia, quanto e' lungo, cosa dice.</summary>
public readonly record struct HostToken(int Start, int Length, string Value)
{
    public int End => Start + Length;
}

/// <summary>
/// Gli indirizzi scritti nel testo: IPv4 (anche con /prefisso), IPv6 e nomi di dominio,
/// in mezzo alle frasi, quanti se ne vuole per riga. Un nome con l'estensione di un file
/// (notes.md, schema.sql) non e' un dominio, e nemmeno un suffisso a maiuscole miste.
/// </summary>
public static class HostTokens
{
    private const string Octet = @"(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)";
    private const string Hex = "[0-9A-Fa-f]{1,4}";

    private static readonly Regex Ipv4 = new(
        $@"(?<![\w.])(?:{Octet}\.){{3}}{Octet}(?:/\d{{1,2}})?(?!\w)(?!\.\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ipv6 = new(
        "(?<![0-9A-Fa-f:.])(?:" + string.Join('|',
            $"(?:{Hex}:){{7}}{Hex}",
            $"(?:{Hex}:){{1,7}}:",
            $"(?:{Hex}:){{1,6}}:{Hex}",
            $"(?:{Hex}:){{1,5}}(?::{Hex}){{1,2}}",
            $"(?:{Hex}:){{1,4}}(?::{Hex}){{1,3}}",
            $"(?:{Hex}:){{1,3}}(?::{Hex}){{1,4}}",
            $"(?:{Hex}:){{1,2}}(?::{Hex}){{1,5}}",
            $"{Hex}:(?::{Hex}){{1,6}}",
            $":(?::{Hex}){{1,7}}",
            $@"(?:{Hex}:){{1,6}}:(?:{Octet}\.){{3}}{Octet}",
            $@"::(?:[Ff]{{4}}(?::0{{1,4}})?:)?(?:{Octet}\.){{3}}{Octet}") +
        @")(?:%[A-Za-z0-9_.-]+)?(?![0-9A-Fa-f:])(?!\.\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Fqdn = new(
        @"(?<![\w.@-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?![\w-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Suffissi che sono estensioni di file, non domini di primo livello.</summary>
    private static readonly HashSet<string> FileSuffixes = new(
        ("md txt rtf exe dll msi png jpg jpeg gif svg webp ico bmp tiff css js jsx ts tsx py rb go rs java " +
         "cs cpp hpp sh bash zsh bat cmd xml json yaml yml toml ini cfg conf log lock env bak tmp swp old " +
         "orig csv tsv pdf doc docx xls xlsx ppt pptx zip tar gz bz xz rar iso img bin dat db sqlite sql " +
         "html htm xaml razor vue svelte mp wav flac ogg ttf otf woff eot").Split(' '),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsDomain(string name)
    {
        if (name.Length > 253)
        {
            return false;
        }

        string suffix = name[(name.LastIndexOf('.') + 1)..];

        return !FileSuffixes.Contains(suffix)
            && (suffix == suffix.ToLowerInvariant() || suffix == suffix.ToUpperInvariant());
    }

    /// <summary>
    /// Il cursore sta dentro l'indirizzo o subito dopo l'ultima lettera: e' una parola che si
    /// sta ancora scrivendo ("nb.zaick" sulla strada di "nb.zaick.net"). Davanti alla prima
    /// lettera no: li' ci si arriva da fuori, e un indirizzo gia' scritto resta tale.
    /// </summary>
    public static bool Touches(HostToken token, int caret) => caret > token.Start && caret <= token.End;

    public static IReadOnlyList<HostToken> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<HostToken> found = [];

        // IPv6 prima di IPv4 e IPv4 prima dei nomi: "::ffff:10.0.0.1" e' un indirizzo solo,
        // e "10.0.0.1" non deve tornare fuori anche come pezzo di un nome.
        foreach (Regex pattern in (Regex[])[Ipv6, Ipv4, Fqdn])
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (ReferenceEquals(pattern, Fqdn) && !IsDomain(match.Value))
                {
                    continue;
                }

                HostToken token = new(match.Index, match.Length, match.Value);

                if (!found.Exists(other => token.Start < other.End && token.End > other.Start))
                {
                    found.Add(token);
                }
            }
        }

        found.Sort((a, b) => a.Start.CompareTo(b.Start));

        return found;
    }

    /// <summary>Il testo e' un indirizzo e nient'altro.</summary>
    public static string? Only(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        IReadOnlyList<HostToken> found = Find(trimmed);

        return found.Count == 1 && found[0].Length == trimmed.Length ? found[0].Value : null;
    }

    /// <summary>L'indirizzo che contiene la colonna data, se ce n'e' uno.</summary>
    public static string? At(string line, int column) =>
        Find(line).FirstOrDefault(token => column >= token.Start && column <= token.End) is { Length: > 0 } hit
            ? hit.Value
            : null;
}
