using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DuckNote.App.Theme;

public static class ThemeTokens
{
    private const string Resource = "DuckNote.App.Theme.tokens.json";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Palettes =
        new(Load);

    public static IReadOnlyCollection<string> Names => (List<string>)["light", "dark"];

    public static void Apply(ResourceDictionary resources, string theme)
    {
        if (!Palettes.Value.TryGetValue(theme, out IReadOnlyDictionary<string, string>? palette))
        {
            palette = Palettes.Value["light"];
        }

        foreach ((string key, string hex) in palette)
        {
            Color colour = Parse(hex);

            if (resources.Contains(key) && resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = colour;
                continue;
            }

            resources.Remove(key);
            resources.Add(key, new SolidColorBrush(colour));
        }

        Finishes.Install(resources, theme);
    }

    public static string Colour(string theme, string key) =>
        Palettes.Value.TryGetValue(theme, out IReadOnlyDictionary<string, string>? palette)
        && palette.TryGetValue(key, out string? hex)
            ? hex
            : "#FF000000";

    public static string FromSystem()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);

            return value is int light && light == 0 ? "dark" : "light";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
            return "light";
        }
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static SolidColorBrush Brush(string hex)
    {
        SolidColorBrush brush = new(Parse(hex));
        brush.Freeze();
        return brush;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Tavolozze non incorporate: {Resource}");

        Dictionary<string, Dictionary<string, string>>? raw =
            JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream);

        if (raw is null || raw.Count == 0)
        {
            throw new InvalidOperationException("Tavolozze illeggibili.");
        }

        return raw.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<string, string>)entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
