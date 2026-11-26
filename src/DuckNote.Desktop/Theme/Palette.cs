using Avalonia;
using DuckNote.Desktop.Ui;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using DuckNote.Notes.Sheets;

namespace DuckNote.Desktop.Theme;

/// <summary>
/// Giorno e notte, come nella versione WPF: i token del tema Duck (tokens.extended.json)
/// diventano pennelli fra le risorse dell'app, sempre gli stessi; cambiando tema ne cambia il
/// colore, con una sfumatura, e ogni DynamicResource si aggiorna da solo. "system" segue il
/// sistema (macOS, Windows, GNOME) anche mentre l'app e' aperta.
/// </summary>
public static class Palette
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);

    /// <summary>Tema applicato: light | dark.</summary>
    public static string Effective { get; private set; } = "light";

    /// <summary>Preferenza: system | light | dark.</summary>
    public static string Preference { get; private set; } = "system";

    public static event Action<string>? Changed;

    public static void Initialize(string preference)
    {
        if (Application.Current?.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) =>
            {
                if (Preference == "system" && SystemTheme() != Effective)
                {
                    Set("system");
                }
            };
        }

        Set(preference, animate: false);
    }

    public static string SystemTheme() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark ? "dark" : "light";

    public static void Toggle() => Set(Effective == "dark" ? "light" : "dark");

    public static void Set(string preference, bool animate = true)
    {
        Preference = preference is "light" or "dark" ? preference : "system";
        string effective = Preference == "system" ? SystemTheme() : Preference;

        if (Application.Current is not { } app)
        {
            return;
        }

        List<(SolidColorBrush Brush, Color From, Color To)> fading = [];

        foreach ((string token, Color colour) in Targets(effective))
        {
            if (Brushes.TryGetValue(token, out SolidColorBrush? brush))
            {
                fading.Add((brush, brush.Color, colour));
                continue;
            }

            brush = new SolidColorBrush(colour);
            Brushes[token] = brush;
            app.Resources[token] = brush;
            app.Resources[token + "Color"] = colour;
        }

        // giorno e notte si sfumano in 480 ms (seno in entrata e uscita), come nella versione
        // WPF: la sfumatura la fa questo codice, un fotogramma alla volta
        Motion.Tween(typeof(Palette), 0, 1, animate ? 480 : 0, t =>
        {
            foreach ((SolidColorBrush brush, Color from, Color to) in fading)
            {
                brush.Color = Mix(from, to, t);
            }
        }, null, Curves.SineInOut);

        app.Resources["AccentFill"] ??= new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#FFD700"), 0), new GradientStop(Color.Parse("#FF8C00"), 1) }
        };

        Alias(app, Targets(effective).GetValueOrDefault("BgHover"));
        app.RequestedThemeVariant = effective == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Effective = effective;
        Changed?.Invoke(effective);
    }

    /// <summary>
    /// Le tabelle (DataGrid di Fluent) prendono i colori Duck: righe scelte d'accento tenue,
    /// intestazioni grigie, niente rettangolo del fuoco sulla cella. Le chiavi sono quelle del
    /// tema della tabella; le risorse dell'app vengono prima di quelle degli stili.
    /// </summary>
    private static void Alias(Application app, Color hover)
    {
        foreach ((string key, string token) in new[]
                 {
                     ("DataGridRowSelectedBackgroundBrush", "AccentSoft"),
                     ("DataGridRowSelectedHoveredBackgroundBrush", "AccentSoft"),
                     ("DataGridRowSelectedUnfocusedBackgroundBrush", "AccentSoft"),
                     ("DataGridRowSelectedHoveredUnfocusedBackgroundBrush", "AccentSoft"),
                     ("DataGridColumnHeaderForegroundBrush", "LabelTertiary"),
                     ("DataGridColumnHeaderHoveredBackgroundBrush", "BgHover"),
                     ("DataGridColumnHeaderPressedBackgroundBrush", "BgPressed"),
                     ("DataGridGridLinesBrush", "SeparatorSoft"),
                     ("DataGridFillerColumnGridLinesBrush", "SeparatorSoft")
                 })
        {
            app.Resources[key] = Brush(token);
        }

        foreach (string key in new[]
                 {
                     "DataGridColumnHeaderBackgroundBrush", "DataGridRowBackgroundBrush", "DataGridCellBackgroundBrush",
                     "DataGridCellFocusVisualPrimaryBrush", "DataGridCellFocusVisualSecondaryBrush", "DataGridCurrencyVisualPrimaryBrush"
                 })
        {
            app.Resources[key] = Avalonia.Media.Brushes.Transparent;
        }

        foreach (string key in new[]
                 {
                     "DataGridRowSelectedBackgroundOpacity", "DataGridRowSelectedHoveredBackgroundOpacity",
                     "DataGridRowSelectedUnfocusedBackgroundOpacity", "DataGridRowSelectedHoveredUnfocusedBackgroundOpacity"
                 })
        {
            app.Resources[key] = 1.0;
        }

        app.Resources["DataGridRowHoveredBackgroundColor"] = hover;
    }

    private static Color Mix(Color from, Color to, double t)
    {
        static byte Channel(byte a, byte b, double t) => (byte)Math.Clamp(Math.Round(a + ((b - a) * t)), 0, 255);

        return Color.FromArgb(Channel(from.A, to.A, t), Channel(from.R, to.R, t), Channel(from.G, to.G, t), Channel(from.B, to.B, t));
    }

    /// <summary>Il pennello di un token (lo stesso oggetto per tutta la vita dell'app).</summary>
    public static IBrush Brush(string token) =>
        Brushes.TryGetValue(token, out SolidColorBrush? brush) ? brush : Avalonia.Media.Brushes.Transparent;

    public static Color Colour(string token) => Brushes.TryGetValue(token, out SolidColorBrush? brush) ? brush.Color : Colors.Transparent;

    /// <summary>I colori del tema applicato, come "#AARRGGBB": per l'editor della nota.</summary>
    public static Dictionary<string, string> Css()
    {
        Dictionary<string, string> css = new(StringComparer.Ordinal);

        foreach ((string token, Color colour) in Targets(Effective))
        {
            // #RRGGBBAA: lo capiscono WebKit, WebView2 e WebKitGTK
            css[token] = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}{colour.A:X2}";
        }

        return css;
    }

    /// <summary>Il colore finale di ogni token nel tema dato, con le finiture della versione WPF.</summary>
    private static Dictionary<string, Color> Targets(string theme)
    {
        bool dark = theme == "dark";
        Dictionary<string, Color> t = ThemeColours.All(theme).ToDictionary(pair => pair.Key, pair => Color.Parse(pair.Value), StringComparer.Ordinal);

        Color Hex(string hex) => Color.Parse(hex);

        t["GlassRim"] = Hex(dark ? "#33FFFFFF" : "#1F000000");
        t["GlassField"] = Hex(dark ? "#40000000" : "#59FFFFFF");

        foreach ((string key, string hex) in dark
                     ? new[] { ("ChartDone", "#3FAE62"), ("ChartRun", "#5B8DEF"), ("ChartWait", "#D67A00"), ("ChartErr", "#D9466A") }
                     : [("ChartDone", "#1F9D45"), ("ChartRun", "#2E6BE6"), ("ChartWait", "#FF8C00"), ("ChartErr", "#C0392B")])
        {
            t[key] = Hex(hex);
        }

        // superfici opache: i token *Glass hanno senso solo sopra una sfocatura
        foreach ((string glass, string solid) in new[] { ("BgWindow", "BgGrouped"), ("BgToolbarGlass", "BgToolbar"), ("BgSidebarGlass", "BgSidebar"), ("BgContentGlass", "BgContent") })
        {
            if (t.TryGetValue(solid, out Color value))
            {
                t[glass] = value;
            }
        }

        if (t.TryGetValue("BgContent", out Color content))
        {
            t["Panel"] = content;
        }

        if (t.TryGetValue("BgFieldAlt", out Color alt))
        {
            t["PanelSoft"] = alt;
        }

        return t;
    }
}
