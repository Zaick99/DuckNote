using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DuckNote.App.Scanning;
using DuckNote.Desktop.Theme;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Scanning;
using MapWeb = DuckNote.Notes.Maps.MapPage;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// La mappa della rete: la pagina web della mappa (DuckNote.Notes/Web, la stessa di Windows) in un
/// WebView a tutta pagina. Questa classe fa da ponte, come per la nota: manda alla pagina le
/// mappe salvate, il tema e gli host della tabella col loro stato (dm.*), e ascolta quello che
/// la pagina dice (le mappe cambiate, i tasti della finestra, un host da vedere nella pagina
/// Rete, un file da esportare).
/// </summary>
public sealed class MapPage : UserControl, IDuckPage
{
    private readonly Workspace _ws;
    private readonly NativeWebView _web = new() { Focusable = true };
    private readonly DispatcherTimer _hosts = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private bool _ready;
    private bool _front;
    private bool _navigated;

    public MapPage(Workspace ws)
    {
        _ws = ws;

        _web.EnvironmentRequested += NotesPage.OnEnvironment;
        _web.WebMessageReceived += (_, e) => OnMessage(e.Body);
        _web.NavigationStarted += (_, e) =>
        {
            // la mappa e' la pagina caricata da qui: nessun clic porta altrove
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

        // durante un'analisi lo stato cambia di continuo: alla mappa si manda a ritmo calmo
        _hosts.Tick += (_, _) =>
        {
            _hosts.Stop();
            PushHosts();
        };

        ws.NoteReplaced += () => Load(focus: false);
        ws.HostsChanged += () =>
        {
            if (!_hosts.IsEnabled)
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
                _web.NavigateToString(MapPage.PageHtml(), new Uri("https://ducknote.invalid/mappa"));
            }
        };
    }

    /// <summary>Un tasto della finestra premuto sulla mappa (F9, Cmd+L...): lo gestisce la finestra.</summary>
    public event Action<string>? WindowKey;

    /// <summary>La mappa chiede la pagina Rete (per analizzare, o per vedere un host).</summary>
    public event Action<string?>? NetworkWanted;

    /// <summary>"Promemoria" sulla scheda di un apparato: una voce nuova del calendario legata a lui.</summary>
    public event Action<object>? ReminderWanted;

    public void OnShown()
    {
        _front = true;

        if (_ready)
        {
            PushHosts();
            GiveKeyboard();
            Call("dm.focus()");
        }
    }

    public void OnHidden() => _front = false;

    private static string PageHtml()
    {
        byte[]? art = null;

        try
        {
            using Stream image = AssetLoader.Open(new Uri("avares://DuckNote/Assets/art/network.png"));
            using MemoryStream copy = new();
            image.CopyTo(copy);
            art = copy.ToArray();
        }
        catch (Exception problem) when (problem is IOException or FileNotFoundException)
        {
            // la mappa vuota resta senza disegno
        }

        return MapWeb.Html(art);
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
            System.Diagnostics.Debug.WriteLine($"mappa: {problem.Message}");
        }
    }

    private static string Js(object? value) => JsonSerializer.Serialize(value);

    private void PushTheme() => Call($"dm.theme({Js(Palette.Css())}, {Js(Palette.Effective == "dark")})");

    private void PushHosts()
    {
        if (_ready && !_ws.IsLocked)
        {
            Call($"dm.hosts({Js(MapFeed.Hosts(_ws.Session, _ws.Groups))}, {Js(MapFeed.Groups(_ws.Groups))})");
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
            ["hosts"] = MapFeed.Hosts(_ws.Session, _ws.Groups),
            ["groups"] = MapFeed.Groups(_ws.Groups)
        });

        Call($"dm.load({_ws.MapJson ?? "null"}, {options})");
    }

    /// <summary>La nota si chiude a chiave: via la mappa dalla pagina, subito.</summary>
    public void Forget() => Call("dm.clear()");

    /// <summary>Le mappe come sono adesso nella pagina, prima di salvare per chiudere o bloccare.</summary>
    public async Task Flush()
    {
        if (!_ready || _ws.IsLocked)
        {
            return;
        }

        try
        {
            if (await _web.InvokeScript("dm.doc()") is { Length: > 2 } raw)
            {
                // InvokeScript restituisce il valore come JSON: una stringa JSON dentro una stringa
                string json = raw.StartsWith('"') ? JsonSerializer.Deserialize<string>(raw) ?? string.Empty : raw;
                _ws.UpdateMap(json);
            }
        }
        catch (Exception problem) when (problem is InvalidOperationException or ObjectDisposedException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"mappa: {problem.Message}");
        }
    }

    /// <summary>Un apparato della mappa al centro (dalla pagina Rete: "mostra sulla mappa").</summary>
    public void Reveal(string host) => Call($"dm.reveal({Js(host)})");

    /// <summary>Le voci del menu Modifica del Mac, sulla mappa.</summary>
    public void Edit(string action)
    {
        switch (action)
        {
            case "undo" or "redo" or "selectAll":
                Call($"dm.edit('{action}')");
                break;

            case "cut" or "copy":
                // il WKWebView copia da se' (anche dal campo nascosto della mappa): la pagina
                // riceve l'evento degli appunti e ci mette gli apparati scelti
                if (!NativeFocus.Edit(_web, action + ":"))
                {
                    Call($"dm.edit('{action}')");
                }

                break;

            case "paste" or "pastePlain":
                if (!NativeFocus.Edit(_web, "paste:"))
                {
                    PasteText();
                }

                break;
        }
    }

    private async void PasteText()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            string? text = await clipboard.TryGetTextAsync();
            Call($"dm.paste({Js(text ?? string.Empty)})");
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

            case "remind" when message["node"]?.GetValue<string>() is { Length: > 0 } node:
                string name = message["name"]?.GetValue<string>() ?? string.Empty;
                ReminderWanted?.Invoke(new Dictionary<string, object?>
                {
                    ["kind"] = "event",
                    ["title"] = name.Length > 0 ? $"Intervento su {name}" : string.Empty,
                    ["link"] = new Dictionary<string, string>
                    {
                        ["kind"] = "node",
                        ["map"] = message["map"]?.GetValue<string>() ?? string.Empty,
                        ["node"] = node,
                        ["name"] = name,
                        ["host"] = message["host"]?.GetValue<string>() ?? string.Empty
                    }
                });
                break;

            case "check" when message["host"]?.GetValue<string>() is { Length: > 0 } host:
                _ = _ws.Analyse(host);
                _ws.Say($"Controllo di {host}...");
                break;

            case "export":
                _ = Export(message);
                break;

            case "error" when message["text"]?.GetValue<string>() is { } text:
                Notices.Current?.Error("Esportazione non riuscita", text);
                break;
        }
    }

    /// <summary>Un'esportazione della pagina (PNG, SVG o CSV) in un file scelto da chi la chiede.</summary>
    private async Task Export(JsonNode message)
    {
        string format = message["format"]?.GetValue<string>() ?? string.Empty;
        string name = message["name"]?.GetValue<string>() ?? "Mappa";
        byte[]? bytes;
        FilePickerFileType type;

        try
        {
            switch (format)
            {
                case "png":
                    bytes = Convert.FromBase64String(message["data"]?.GetValue<string>() ?? string.Empty);
                    type = new FilePickerFileType("Immagine PNG") { Patterns = ["*.png"], AppleUniformTypeIdentifiers = ["public.png"], MimeTypes = ["image/png"] };
                    break;

                case "svg":
                    bytes = System.Text.Encoding.UTF8.GetBytes(message["text"]?.GetValue<string>() ?? string.Empty);
                    type = new FilePickerFileType("Disegno SVG") { Patterns = ["*.svg"], AppleUniformTypeIdentifiers = ["public.svg-image"], MimeTypes = ["image/svg+xml"] };
                    break;

                case "csv":
                    List<string> header = message["header"]?.AsArray().Select(cell => cell?.GetValue<string>() ?? string.Empty).ToList() ?? [];
                    List<IReadOnlyList<string>> rows = message["rows"]?.AsArray()
                        .Select(row => (IReadOnlyList<string>)(row?.AsArray().Select(cell => cell?.GetValue<string>() ?? string.Empty).ToList() ?? []))
                        .ToList() ?? [];
                    bytes = CsvTable.Write(header, rows);
                    type = new FilePickerFileType("CSV (separato da punto e virgola)") { Patterns = ["*.csv"], AppleUniformTypeIdentifiers = ["public.comma-separated-values-text"] };
                    break;

                default:
                    return;
            }
        }
        catch (Exception problem) when (problem is FormatException or InvalidOperationException)
        {
            Notices.Current?.Error("Esportazione non riuscita", problem.Message);
            return;
        }

        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta la mappa",
            SuggestedFileName = name,
            DefaultExtension = format,
            FileTypeChoices = [type]
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using (Stream stream = await file.OpenWriteAsync())
            {
                await stream.WriteAsync(bytes);
            }

            Notices.Current?.Success("Mappa esportata", file.Name);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            Notices.Current?.Error("Esportazione non riuscita", problem.Message);
        }
    }
}
