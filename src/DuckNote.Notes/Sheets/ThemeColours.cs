using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DuckNote.Notes.Sheets;

/// <summary>
/// I colori del tema Duck, chiaro e scuro, letti dai token (tokens.extended.json). Servono per
/// passare fra il file della nota, che porta colori scritti ("#FF1F9D45"), e i token (Green)
/// con cui la nota li usa: un colore torna token, un token si scrive col colore del tema chiaro.
/// </summary>
public static class ThemeColours
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Themes = new(Load);

    /// <summary>
    /// I token che il testo puo' portare, in ordine di precedenza: due token con lo stesso colore
    /// in un tema si risolvono a favore del primo (lo stesso ordine della versione WPF).
    /// </summary>
    private static readonly string[] Tinted =
    [
        "Label", "LabelSecondary", "LabelTertiary", "LabelQuaternary", "CodeFg", "CodeBg", "Highlight", "HighlightFg",
        "TableHeaderBg", "TableBorder", "Separator", "Blue", "Green", "Red", "Orange", "Purple", "Teal", "Accent",
        "Pink", "Yellow", "Indigo"
    ];

    /// <summary>I colori che si possono dare al testo: nome mostrato e token.</summary>
    public static readonly (string Name, string Token)[] TextColours =
    [
        ("Automatico", "Label"), ("Grigio", "LabelSecondary"), ("Rosso", "Red"), ("Arancio", "Orange"),
        ("Giallo", "Yellow"), ("Verde", "Green"), ("Verde acqua", "Teal"), ("Blu", "Blue"),
        ("Indaco", "Indigo"), ("Viola", "Purple"), ("Rosa", "Pink")
    ];

    private static readonly Lazy<Dictionary<string, string>> Known = new(() =>
    {
        Dictionary<string, string> known = new(StringComparer.OrdinalIgnoreCase);

        foreach (string theme in (string[])["light", "dark"])
        {
            foreach (string token in Tinted)
            {
                if (Hex(theme, token) is { } hex)
                {
                    known.TryAdd(hex, token);
                }
            }
        }

        return known;
    });

    /// <summary>Tutti i token di un tema ("light" o "dark"), come "#AARRGGBB".</summary>
    public static IReadOnlyDictionary<string, string> All(string theme) =>
        Themes.Value.TryGetValue(theme, out Dictionary<string, string>? tokens)
            ? tokens.ToDictionary(pair => pair.Key, pair => Argb(pair.Value))
            : new Dictionary<string, string>();

    /// <summary>Il colore di un token nel tema, come "#AARRGGBB" (null se il token non c'e').</summary>
    public static string? Hex(string theme, string token) =>
        Themes.Value.TryGetValue(theme, out Dictionary<string, string>? tokens) && tokens.TryGetValue(token, out string? value)
            ? Argb(value)
            : null;

    /// <summary>Il colore che il file della nota porta per un token: quello del tema chiaro.</summary>
    public static string Written(string token) => Hex("light", token) ?? "#FF1F2328";

    /// <summary>Il token di un colore scritto nel file, se e' uno dei colori del tema.</summary>
    public static string? TokenOf(string? colour) =>
        colour is null ? null : Known.Value.GetValueOrDefault(Argb(colour.Trim()));

    /// <summary>"#RGB", "#RRGGBB", "#AARRGGBB" e "Transparent" come "#AARRGGBB" maiuscolo.</summary>
    public static string Argb(string colour)
    {
        string hex = colour.TrimStart('#').ToUpperInvariant();

        if (colour.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
        {
            return "#00FFFFFF";
        }

        return hex.Length switch
        {
            3 => "#FF" + string.Concat(hex.Select(c => $"{c}{c}")),
            6 => "#FF" + hex,
            8 => "#" + hex,
            _ => "#" + hex
        };
    }

    /// <summary>Il colore e' trasparente (o quasi): uno sfondo cosi' non e' uno sfondo.</summary>
    public static bool IsClear(string? colour)
    {
        if (colour is null)
        {
            return true;
        }

        string argb = Argb(colour);

        return argb.Length == 9
            && int.TryParse(argb.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int alpha)
            && alpha < 8;
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DuckNote.Notes.tokens.json");

        if (stream is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream) ?? [];
    }
}
