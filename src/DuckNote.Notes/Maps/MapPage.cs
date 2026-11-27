using System.Reflection;
using DuckNote.Notes.Sheets;

namespace DuckNote.Notes.Maps;

/// <summary>
/// La pagina della mappa per il WebView: HTML, stile e script in un file solo (Web/dist/map.js e
/// map.css, costruiti da Web con "npm run build"), con l'illustrazione della mappa vuota.
/// La stessa pagina va nel WKWebView del Mac e nel WebView2 di Windows.
/// </summary>
public static class MapPage
{
    private static readonly Lazy<string> Script = new(() => Resource("DuckNote.Notes.map.js"));
    private static readonly Lazy<string> Style = new(() => Resource("DuckNote.Notes.map.css"));

    /// <summary>La pagina intera; <paramref name="emptyArt"/> e' il PNG dell'invito della mappa vuota.</summary>
    public static string Html(byte[]? emptyArt = null)
    {
        string art = emptyArt is { Length: > 0 } ? $"<style>:root{{--empty-art:url(data:image/png;base64,{Convert.ToBase64String(emptyArt)})}}</style>" : string.Empty;

        return "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\">"
               + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
               + "<style>" + Style.Value + "</style>" + art + "</head><body>"
               + "<script>" + Script.Value + "</script></body></html>";
    }

    /// <summary>
    /// I colori del tema come li vuole la pagina (dm.theme): "#RRGGBBAA" per token. I token
    /// "di vetro" diventano le superfici piene, come nell'editor della nota: nel WebView sotto non
    /// c'e' niente da far trasparire.
    /// </summary>
    public static Dictionary<string, string> ThemeCss(string theme)
    {
        IReadOnlyDictionary<string, string> tokens = ThemeColours.All(theme);
        Dictionary<string, string> css = new(StringComparer.Ordinal);

        foreach ((string token, string argb) in tokens)
        {
            css[token] = Rgba(argb);
        }

        foreach ((string glass, string solid) in new[] { ("BgWindow", "BgGrouped"), ("BgToolbarGlass", "BgToolbar"), ("BgSidebarGlass", "BgSidebar"), ("BgContentGlass", "BgContent"), ("Panel", "BgContent"), ("PanelSoft", "BgFieldAlt") })
        {
            if (css.TryGetValue(solid, out string? value))
            {
                css[glass] = value;
            }
        }

        return css;
    }

    /// <summary>"#AARRGGBB" (WPF) come "#RRGGBBAA" (CSS).</summary>
    public static string Rgba(string argb)
    {
        string hex = argb.TrimStart('#');
        return hex.Length == 8 ? $"#{hex[2..]}{hex[..2]}" : $"#{hex}";
    }

    private static string Resource(string name)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return stream is null ? string.Empty : new StreamReader(stream).ReadToEnd();
    }
}
