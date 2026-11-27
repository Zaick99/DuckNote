using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DuckNote.App.Scanning;
using DuckNote.Desktop.Theme;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Maps;
using CalendarWeb = DuckNote.Notes.Calendar.CalendarPage;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// Il calendario della nota: la pagina web del calendario (DuckNote.Notes/Web, la stessa di
/// Windows) in un WebView a tutta pagina. Il calendario vero sta nel Workspace: la pagina manda i
/// cambiamenti (op) e riceve il calendario aggiornato (dc.data), gli host e gli apparati della
/// mappa per i legami, il tema. Un legame cliccato apre la pagina Rete o la mappa.
/// </summary>
public sealed class CalendarPage : UserControl, IDuckPage
{
    private readonly Workspace _ws;
    private readonly NativeWebView _web = new() { Focusable = true };
    private readonly DispatcherTimer _hosts = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /// <summary>Quello che si e' chiesto prima che la pagina fosse pronta (una voce da scrivere, un giorno da mostrare).</summary>
    private readonly List<string> _waiting = [];

    private bool _ready;
    private bool _front;
    private bool _navigated;

    public CalendarPage(Workspace ws)
    {
        _ws = ws;

        _web.EnvironmentRequested += NotesPage.OnEnvironment;
        _web.WebMessageReceived += (_, e) => OnMessage(e.Body);
        _web.NavigationStarted += (_, e) =>
        {
            if (e.Request is { } uri && uri.Scheme is "http" or "https" && !uri.Host.EndsWith("ducknote.invalid", StringComparison.Ordinal))
            {
                e.Cancel = true;
            }
        };
        _web.NewWindowRequested += (_, e) => e.Handled = true;

        Border paper = new() { Child = _web, ClipToBounds = true, BorderThickness = new Thickness(0, 1, 0, 0) };
        paper.Bind(Border.BackgroundProperty, paper.GetResourceObservable("BgContent"));
        paper.Bind(Border.BorderBrushProperty, paper.GetResourceObservable("Separator"));
        Content = paper;

        _hosts.Tick += (_, _) =>
        {
            _hosts.Stop();
            PushHosts();
        };

        ws.NoteReplaced += () => Dispatcher.UIThread.Post(Load);
        ws.CalendarChanged += () => Dispatcher.UIThread.Post(PushData);
        ws.HostsChanged += () =>
        {
            if (_front && !_hosts.IsEnabled)
            {
                _hosts.Start();
            }
        };
        Palette.Changed += _ => PushTheme();

        AttachedToVisualTree += (_, _) =>
        {
            if (!_navigated)
            {
                _navigated = true;
                _web.NavigateToString(CalendarWeb.Html(), new Uri("https://ducknote.invalid/calendario"));
            }
        };
    }

    /// <summary>Un tasto della finestra premuto nel calendario (F9, Cmd+L...): lo gestisce la finestra.</summary>
    public event Action<string>? WindowKey;

    /// <summary>Un legame con un host: lo si vede nella pagina Rete.</summary>
    public event Action<string>? HostWanted;

    /// <summary>Un legame con un apparato: lo si vede sulla mappa (indirizzo o identificativo dell'apparato).</summary>
    public event Action<string>? DeviceWanted;

    public void OnShown()
    {
        _front = true;

        if (_ready)
        {
            PushHosts();
            PushDevices();
            GiveKeyboard();
            Call("dc.focus()");
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

    /// <summary>Le voci del menu Modifica del Mac, nei campi del calendario.</summary>
    public void Edit(string action)
    {
        switch (action)
        {
            case "undo" or "redo" or "selectAll":
                Call($"dc.edit('{action}')");
                break;

            case "cut" or "copy" or "paste" or "pastePlain":
                NativeFocus.Edit(_web, (action == "pastePlain" ? "paste" : action) + ":");
                break;
        }
    }

    private void GiveKeyboard()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager is { } focus && ReferenceEquals(focus.GetFocusedElement(), _web))
        {
            focus.Focus(null!, NavigationMethod.Unspecified, KeyModifiers.None);
        }

        _web.Focus();
        NativeFocus.Give(_web);
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
        if (!_ready)
        {
            return;
        }

        try
        {
            await _web.InvokeScript(script);
        }
        catch (Exception problem) when (problem is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Debug.WriteLine($"calendario: {problem.Message}");
        }
    }

    private static string Js(object? value) => JsonSerializer.Serialize(value);

    private void PushTheme() => Call($"dc.theme({Js(Palette.Css())}, {Js(Palette.Effective == "dark")})");

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
                // la pagina l'ha gia' fatto da se': se il calendario non cambia (sola lettura,
                // nota bloccata) la pagina torna a com'era
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
