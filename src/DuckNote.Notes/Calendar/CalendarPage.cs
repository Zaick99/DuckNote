using System.Reflection;

namespace DuckNote.Notes.Calendar;

/// <summary>
/// La pagina del calendario per il WebView: HTML, stile e script in un file solo (Web/dist/
/// calendar.js e calendar.css, costruiti da Web con "npm run build"). La stessa pagina va nel
/// WKWebView del Mac e nel WebView2 di Windows; i colori arrivano con dc.theme, come per la mappa
/// (Maps.MapPage.ThemeCss).
/// </summary>
public static class CalendarPage
{
    private static readonly Lazy<string> Script = new(() => Resource("DuckNote.Notes.calendar.js"));
    private static readonly Lazy<string> Style = new(() => Resource("DuckNote.Notes.calendar.css"));

    public static string Html() =>
        "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\">"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        + "<style>" + Style.Value + "</style></head><body>"
        + "<script>" + Script.Value + "</script></body></html>";

    private static string Resource(string name)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return stream is null ? string.Empty : new StreamReader(stream).ReadToEnd();
    }
}
