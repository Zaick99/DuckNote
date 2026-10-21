using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;

namespace DuckNote.App.Theme;

/// <summary>
/// La tavolozza letta come dati, per chi ha bisogno del colore di un tema preciso (anche di
/// quello non attivo). I pennelli vivi li crea solo <see cref="ThemeManager"/>.
/// </summary>
public static class ThemeTokens
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Palettes = new(Load);

    public static string Colour(string theme, string key) =>
        Palettes.Value.TryGetValue(theme, out Dictionary<string, string>? palette) && palette.TryGetValue(key, out string? hex)
            ? hex
            : "#FF000000";

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.tokens.extended.json")
            ?? throw new InvalidOperationException("Tavolozze non incorporate.");

        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)?
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            ?? throw new InvalidOperationException("Tavolozze illeggibili.");
    }
}
