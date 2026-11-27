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
using DuckNote.Notes.Maps;
using DuckNote.Notes.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using CalendarWeb = DuckNote.Notes.Calendar.CalendarPage;
using MapWeb = DuckNote.Notes.Maps.MapPage;

namespace DuckNote.App.Pages;

/// <summary>
/// Il calendario della nota: la pagina web del calendario (DuckNote.Notes/Web, la stessa del Mac)
/// in un WebView2 "di composizione", come la mappa (si vede anche nella finestra trasparente).
/// Il calendario vero sta nel Workspace: la pagina manda i cambiamenti (op) e riceve il calendario
/// aggiornato (dc.data), gli host e gli apparati della mappa per i legami, il tema. Un legame
/// cliccato apre la pagina Rete o la mappa.
/// </summary>
public sealed class CalendarPage : UserControl, IDuckPage
{
    private const string Address = "https://ducknote.invalid/calendario";

    private readonly Workspace _ws;
    private readonly SafeWebView _web = new() { Focusable = true };
    private readonly Border _missing;
    private readonly TextBlock _missingText = new();
    private readonly DispatcherTimer _hosts = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /// <summary>Quello che si e' chiesto prima che la pagina fosse pronta (una voce da scrivere, un giorno da mostrare).</summary>
    private readonly List<string> _waiting = [];

    private bool _starting;
    private bool _ready;
    private bool _front;

    public CalendarPage(Workspace ws)
    {
        _ws = ws;
        _web.CreationProperties = new CoreWebView2CreationProperties
        {
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

        ws.NoteReplaced += () => Dispatcher.BeginInvoke(Load);
        ws.CalendarChanged += () => Dispatcher.BeginInvoke(PushData);
        ws.HostsChanged += () =>
        {
            if (_front && !_hosts.IsEnabled)
            {
                _hosts.Start();
            }
        };
        ThemeManager.Changed += _ => PushTheme();

        Loaded += async (_, _) => await Start();
    }

    /// <summary>Un tasto della finestra premuto nel calendario (F9, Ctrl+L...): lo gestisce la finestra.</summary>
    public event Action<string>? WindowKey;

    /// <summary>Un legame con un host: lo si vede nella pagina Rete.</summary>
    public event Action<string>? HostWanted;

    /// <summary>Un legame con un apparato: lo si vede sulla mappa (identificativo dell'apparato o indirizzo).</summary>
    public event Action<string>? DeviceWanted;

    public void OnShown()
    {
        _front = true;

        if (_ready)
        {
            PushHosts();
            PushDevices();
            _web.Focus();
            Call("dc.focus()");
        }
        else if (!_starting && IsLoaded && _web.Problem is null)
        {
            _ = Start();
        }
    }

    public void OnHidden() => _front = false;

    /// <summary>Una voce nuova gia' avviata, da Rete o dalla mappa: { kind, title, date, link }.</summary>
    public void Compose(object seed) => Later($"dc.compose({Js(seed)})");

    /// <summary>Un giorno, e la voce "id@giorno" se c'e' (da un avviso).</summary>
    public void Show(DateOnly day, string? key = null) =>
        Later($"dc.show({Js(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))}, {Js(key)})");

    /// <summary>La nota si chiude a chiave: via il calendario dalla pagina, subito.</summary>
    public void Forget() => Call("dc.clear()");

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
                ? core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(CalendarWeb.Html())), 200, "OK", "Content-Type: text/html; charset=utf-8")
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
            // il motore si e' fermato: si ricarica la pagina, il calendario e' al sicuro nella nota
            _ready = false;
            _web.CoreWebView2?.Navigate(Address);
        });

        core.Navigate(Address);
    }

    private Border Missing()
    {
        TextBlock title = new() { Text = "Il calendario non e' partito", FontSize = 17, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Label");
        _missingText.TextWrapping = TextWrapping.Wrap;
        _missingText.TextAlignment = TextAlignment.Center;
        _missingText.Margin = new Thickness(0, 8, 0, 0);
        _missingText.MaxWidth = 460;
        _missingText.SetResourceReference(TextBlock.ForegroundProperty, "LabelSecondary");

        return new Border
        {
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { title, _missingText } }
        };
    }

    private void ShowMissing(Exception problem)
    {
        _missingText.Text = problem is WebView2RuntimeNotFoundException
            ? "Serve Microsoft Edge WebView2, gia' in Windows 10 e 11 aggiornati; se manca si installa gratis da Microsoft (\"WebView2 Runtime\"). I promemoria suonano lo stesso, e il calendario salvato nella nota resta com'e'."
            : $"Il motore della pagina (WebView2) ha dato un errore: {problem.Message} I promemoria suonano lo stesso, e il calendario salvato nella nota resta com'e'.";
        _ready = false;
        _web.Visibility = Visibility.Collapsed;
        _missing.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ verso la pagina

    private void Later(string script)
    {
        if (_ready)
        {
            Call(script);
        }
        else
        {
            _waiting.Add(script);
        }
    }

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
            System.Diagnostics.Debug.WriteLine($"calendario: {problem.Message}");
        }
    }

    private static string Js(object? value) => JsonSerializer.Serialize(value);

    private void PushTheme() => Call($"dc.theme({Js(MapWeb.ThemeCss(ThemeManager.Effective))}, {Js(ThemeManager.Effective == "dark")})");

    private void PushHosts()
    {
        if (_ready && !_ws.IsLocked)
        {
            Call($"dc.hosts({Js(MapFeed.Hosts(_ws.Session, _ws.Groups))})");
        }
    }

    private void PushDevices()
    {
        if (_ready && !_ws.IsLocked)
        {
            Call($"dc.devices({Js(NetworkMap.DeviceList(_ws.Book.Map))})");
        }
    }

    private void PushData()
    {
        if (_ready && !_ws.IsLocked)
        {
            Call($"dc.data({_ws.Calendar.Json()})");
        }
    }

    /// <summary>Il calendario della nota nella pagina (all'apertura, allo sblocco, al blocco).</summary>
    private void Load()
    {
        if (!_ready)
        {
            return;
        }

        if (_ws.IsLocked)
        {
            Call("dc.clear()");
            return;
        }

        string options = Js(new Dictionary<string, object?>
        {
            ["readonly"] = _ws.IsDamaged,
            ["hosts"] = MapFeed.Hosts(_ws.Session, _ws.Groups),
            ["devices"] = NetworkMap.DeviceList(_ws.Book.Map)
        });

        Call($"dc.load({_ws.Calendar.Json()}, {options})");
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
                Load();

                foreach (string script in _waiting)
                {
                    Call(script);
                }

                _waiting.Clear();
                break;

            case "op" when message["op"] is JsonObject change:
                if (!_ws.ChangeCalendar(change))
                {
                    PushData();
                }

                break;

            case "activity":
                _ws.Activity();
                break;

            case "key" when message["key"]?.GetValue<string>() is { } key:
                WindowKey?.Invoke(key);
                break;

            case "reveal" when message["host"]?.GetValue<string>() is { Length: > 0 } host:
                HostWanted?.Invoke(host);
                break;

            case "node":
                string node = message["node"]?.GetValue<string>() ?? string.Empty;
                string address = message["host"]?.GetValue<string>() ?? string.Empty;

                if (node.Length > 0 || address.Length > 0)
                {
                    DeviceWanted?.Invoke(node.Length > 0 ? node : address);
                }

                break;
        }
    }
}
