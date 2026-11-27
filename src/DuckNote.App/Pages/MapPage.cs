using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DuckNote.App.Scanning;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.Notes.Scanning;
using DuckNote.Notes.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using MapWeb = DuckNote.Notes.Maps.MapPage;

namespace DuckNote.App.Pages;

/// <summary>
/// La mappa della rete: la pagina web della mappa (Maps/Web, la stessa del Mac) in un WebView2.
/// La finestra di DuckNote e' trasparente (angoli tondi, vetro): un WebView2 classico, che e' una
/// finestra figlia, li' dentro non si vedrebbe. Il controllo "di composizione" invece disegna
/// dentro WPF, e ci si possono mettere sopra avvisi e menu. La pagina si serve da memoria su un
/// indirizzo che non esiste (ducknote.invalid): niente file su disco, niente rete; il profilo
/// del WebView e' privato (InPrivate), cosi' della mappa non resta nulla fuori dalla nota.
/// </summary>
public sealed class MapPage : UserControl, IDuckPage
{
    private const string Address = "https://ducknote.invalid/mappa";

    private readonly Workspace _ws;
    private readonly SafeWebView _web = new() { Focusable = true };
    private readonly Border _missing;
    private readonly TextBlock _missingTitle = new();
    private readonly TextBlock _missingText = new();
    private readonly DispatcherTimer _hosts = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private bool _starting;
    private bool _ready;
    private bool _front;

    public MapPage(Workspace ws)
    {
        _ws = ws;
        _web.CreationProperties = new CoreWebView2CreationProperties
        {
            // il motore del WebView ha bisogno di una cartella sua anche in privato: fuori dai
            // dati di DuckNote e fuori dalla cartella del programma (che puo' essere in sola lettura)
            UserDataFolder = AppPaths.WebEngine,
            IsInPrivateModeEnabled = true,
            Language = "it-IT"
        };
        _web.SetResourceReference(BackgroundProperty, "BgContent");

        _missing = Missing();
        _web.Failed += ShowMissing;
        Grid root = new();
        root.Children.Add(_web);
        root.Children.Add(_missing);
        Content = root;
        SetResourceReference(BackgroundProperty, "BgContent");

        _hosts.Tick += (_, _) =>
        {
            _hosts.Stop();
            PushHosts();
        };

        ws.NoteReplaced += () => Dispatcher.BeginInvoke(() => Load(focus: false));
        ws.HostsChanged += () =>
        {
            if (!_hosts.IsEnabled)
            {
                _hosts.Start();
            }
        };
        ThemeManager.Changed += _ => PushTheme();

        Loaded += async (_, _) => await Start();
    }

    /// <summary>Un tasto della finestra premuto sulla mappa (F9, Ctrl+L...): lo gestisce la finestra.</summary>
    public event Action<string>? WindowKey;

    /// <summary>La mappa chiede la pagina Rete (per analizzare, o per vedere un host).</summary>
    public event Action<string?>? NetworkWanted;

    public void OnShown()
    {
        _front = true;

        if (_ready)
        {
            PushHosts();
            _web.Focus();
            Call("dm.focus()");
        }
        else if (!_starting && IsLoaded && _web.Problem is null)
        {
            // il motore non c'e' (non e' ancora partito, o si e' fermato): riparte
            _ = Start();
        }
    }

    public void OnHidden() => _front = false;

    // ------------------------------------------------------------------ il WebView

    private async Task Start()
    {
        if (_starting || _web.Problem is not null)
        {
            return;
        }

        _starting = true;

        try
        {
            await _web.EnsureCoreWebView2Async();
        }
        catch (Exception problem) when (SafeWebView.IsEngineProblem(problem) || problem is IOException or UnauthorizedAccessException)
        {
            _starting = false;
            ShowMissing(problem);
            return;
        }

        CoreWebView2 core = _web.CoreWebView2;
        CoreWebView2Settings settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsWebMessageEnabled = true;

        // la pagina arriva da memoria; tutto il resto della rete e' chiuso
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            e.Response = e.Request.Uri.Equals(Address, StringComparison.Ordinal)
                ? core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(PageHtml())), 200, "OK", "Content-Type: text/html; charset=utf-8")
                : core.Environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.Equals(Address, StringComparison.Ordinal))
            {
                e.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += (_, e) => OnMessage(e.TryGetWebMessageAsString());
        core.ProcessFailed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            // il motore si e' fermato: si ricarica la pagina, la mappa e' al sicuro nella nota
            _ready = false;
            _web.CoreWebView2?.Navigate(Address);
        });

        core.Navigate(Address);
    }

    private static string PageHtml()
    {
        byte[]? art = null;

        try
        {
            using Stream? image = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/art/network.png"))?.Stream;

            if (image is not null)
            {
                using MemoryStream copy = new();
                image.CopyTo(copy);
                art = copy.ToArray();
            }
        }
        catch (IOException)
        {
            // la mappa vuota resta senza disegno
        }

        return MapWeb.Html(art);
    }

    /// <summary>Senza il motore WebView2 (Windows vecchi o ridotti) la mappa dice cosa manca, invece di fermare la finestra.</summary>
    private Border Missing()
    {
        _missingTitle.FontSize = 17;
        _missingTitle.FontWeight = FontWeights.Bold;
        _missingTitle.TextAlignment = TextAlignment.Center;
        _missingTitle.SetResourceReference(TextBlock.ForegroundProperty, "Label");
        _missingText.TextWrapping = TextWrapping.Wrap;
        _missingText.TextAlignment = TextAlignment.Center;
        _missingText.Margin = new Thickness(0, 8, 0, 0);
        _missingText.MaxWidth = 460;
        _missingText.SetResourceReference(TextBlock.ForegroundProperty, "LabelSecondary");

        return new Border
        {
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { _missingTitle, _missingText } }
        };
    }

    private void ShowMissing(Exception problem)
    {
        if (problem is WebView2RuntimeNotFoundException)
        {
            _missingTitle.Text = "Per la mappa serve Microsoft Edge WebView2";
            _missingText.Text = "E' gia' in Windows 10 e 11 aggiornati. Se manca si installa gratis da Microsoft (\"WebView2 Runtime\"); poi riapri DuckNote. Il resto di DuckNote funziona anche senza.";
        }
        else
        {
            _missingTitle.Text = "La mappa non e' partita";
            _missingText.Text = $"Il motore della pagina (WebView2) ha dato un errore: {problem.Message} Aggiorna Windows e WebView2 Runtime, poi riapri DuckNote. Il resto di DuckNote funziona anche senza; la mappa salvata nella nota resta com'e'.";
        }

        _ready = false;
        _web.Visibility = Visibility.Collapsed;
        _missing.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ verso la pagina

    private async void Call(string script)
    {
        if (!_ready || _web.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await _web.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception problem) when (problem is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Debug.WriteLine($"mappa: {problem.Message}");
        }
    }

    private static string Js(object? value) => JsonSerializer.Serialize(value);

    private void PushTheme() => Call($"dm.theme({Js(MapWeb.ThemeCss(ThemeManager.Effective))}, {Js(ThemeManager.Effective == "dark")})");

    private void PushHosts()
    {
        if (_ready && !_ws.IsLocked)
        {
            Call($"dm.hosts({Js(MapFeed.Hosts(_ws.Session))})");
        }
    }

    /// <summary>Le mappe della nota nella pagina (all'apertura, allo sblocco, al blocco).</summary>
    private void Load(bool focus)
    {
        if (!_ready)
        {
            return;
        }

        if (_ws.IsLocked)
        {
            Call("dm.clear()");
            return;
        }

        string options = Js(new Dictionary<string, object?>
        {
            ["readonly"] = _ws.IsDamaged,
            ["focus"] = focus && _front,
            ["hosts"] = MapFeed.Hosts(_ws.Session)
        });

        Call($"dm.load({_ws.MapJson ?? "null"}, {options})");
    }

    /// <summary>La nota si chiude a chiave: via la mappa dalla pagina, subito.</summary>
    public void Forget() => Call("dm.clear()");

    /// <summary>Le mappe come sono adesso nella pagina, prima di salvare per chiudere o bloccare.</summary>
    public async Task Flush()
    {
        if (!_ready || _ws.IsLocked || _web.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            string raw = await _web.CoreWebView2.ExecuteScriptAsync("dm.doc()");

            if (raw is { Length: > 2 })
            {
                // ExecuteScriptAsync restituisce il valore come JSON: una stringa JSON dentro una stringa
                string json = raw.StartsWith('"') ? JsonSerializer.Deserialize<string>(raw) ?? string.Empty : raw;
                _ws.UpdateMap(json);
            }
        }
        catch (Exception problem) when (problem is InvalidOperationException or ObjectDisposedException or JsonException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Debug.WriteLine($"mappa: {problem.Message}");
        }
    }

    // ------------------------------------------------------------------ dalla pagina

    private void OnMessage(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        JsonNode? message;

        try
        {
            message = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return;
        }

        switch (message?["t"]?.GetValue<string>())
        {
            case "ready":
                _ready = true;
                PushTheme();
                Load(focus: _front);
                break;

            case "changed" when message["doc"] is { } doc:
                _ws.UpdateMap(doc.ToJsonString());
                break;

            case "activity":
                _ws.Activity();
                break;

            case "key" when message["key"]?.GetValue<string>() is { } key:
                WindowKey?.Invoke(key);
                break;

            case "network":
                NetworkWanted?.Invoke(null);
                break;

            case "reveal" when message["host"]?.GetValue<string>() is { Length: > 0 } host:
                NetworkWanted?.Invoke(host);
                break;

            case "check" when message["host"]?.GetValue<string>() is { Length: > 0 } host:
                _ = _ws.Analyse(host);
                _ws.Say($"Controllo di {host}...");
                break;

            case "export":
                Export(message);
                break;

            case "error" when message["text"]?.GetValue<string>() is { } text:
                Toast.Error("Esportazione non riuscita", text);
                break;
        }
    }

    /// <summary>Un'esportazione della pagina (PNG, SVG o CSV) in un file scelto da chi la chiede.</summary>
    private void Export(JsonNode message)
    {
        string format = message["format"]?.GetValue<string>() ?? string.Empty;
        string name = message["name"]?.GetValue<string>() ?? "Mappa";
        byte[] bytes;
        string filter;

        try
        {
            switch (format)
            {
                case "png":
                    bytes = Convert.FromBase64String(message["data"]?.GetValue<string>() ?? string.Empty);
                    filter = "Immagine PNG|*.png";
                    break;

                case "svg":
                    bytes = Encoding.UTF8.GetBytes(message["text"]?.GetValue<string>() ?? string.Empty);
                    filter = "Disegno SVG|*.svg";
                    break;

                case "csv":
                    List<string> header = message["header"]?.AsArray().Select(cell => cell?.GetValue<string>() ?? string.Empty).ToList() ?? [];
                    List<IReadOnlyList<string>> rows = message["rows"]?.AsArray()
                        .Select(row => (IReadOnlyList<string>)(row?.AsArray().Select(cell => cell?.GetValue<string>() ?? string.Empty).ToList() ?? []))
                        .ToList() ?? [];
                    bytes = CsvTable.Write(header, rows);
                    filter = "CSV (separato da punto e virgola)|*.csv";
                    break;

                default:
                    return;
            }
        }
        catch (Exception problem) when (problem is FormatException or InvalidOperationException)
        {
            Toast.Error("Esportazione non riuscita", problem.Message);
            return;
        }

        SaveFileDialog pick = new() { Title = "Esporta la mappa", Filter = filter, FileName = name };

        if (pick.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(pick.FileName, bytes);
            Toast.Success("Mappa esportata", Path.GetFileName(pick.FileName));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            Toast.Error("Esportazione non riuscita", problem.Message);
        }
    }
}
