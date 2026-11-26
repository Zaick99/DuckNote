using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using DuckNote.Desktop.Notes;
using DuckNote.Desktop.Theme;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Sheets;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// La nota: il foglio aperto e' un editor web (ProseMirror) in un WebView, a tutta pagina.
/// Questa classe e' il ponte: manda all'editor il foglio, il tema, lo zoom e lo stato degli
/// indirizzi (dn.* via InvokeScript) e ascolta quello che l'editor dice (un messaggio JSON per
/// volta: il foglio cambiato, lo stato della barra, il titolo, il destro, i tasti della finestra).
/// La barra di formattazione e l'indice dei fogli li mette la finestra al loro posto.
/// </summary>
public sealed partial class NotesPage : UserControl, IDuckPage
{
    private readonly Workspace _ws;
    private readonly NativeWebView _web = new() { Focusable = true };
    private readonly Border _paper;
    private readonly DispatcherTimer _sync = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>L'indirizzo che si sta ancora scrivendo: non entra in tabella finche' non e' finito.</summary>
    private string? _typing;

    private bool _ready;
    private bool _front;
    private Sheet? _sheet;

    /// <summary>Il foglio mandato all'editor e non ancora confermato: i suoi "changed" non contano.</summary>
    private int _loading;

    public NotesPage(Workspace ws)
    {
        _ws = ws;

        _web.EnvironmentRequested += OnEnvironment;
        _web.WebMessageReceived += (_, e) => OnMessage(e.Body);
        _web.NavigationStarted += (_, e) =>
        {
            // il foglio e' la pagina caricata da qui: nessun clic porta altrove
            if (e.Request is { } uri && uri.Scheme is "http" or "https" && !uri.Host.EndsWith("ducknote.invalid", StringComparison.Ordinal))
            {
                e.Cancel = true;
            }
        };
        _web.NewWindowRequested += (_, e) => e.Handled = true;

        _paper = new Border { Child = _web, ClipToBounds = true };
        _paper.Bind(Border.BackgroundProperty, _paper.GetResourceObservable("NotePaper"));
        _paper.Bind(Border.BorderBrushProperty, _paper.GetResourceObservable("Separator"));
        _paper.BorderThickness = new Thickness(0, 1, 0, 0);
        Content = _paper;

        _sync.Tick += (_, _) =>
        {
            _sync.Stop();
            _ws.SyncHosts(_typing);
        };

        ws.NoteReplaced += OnNoteReplaced;
        ws.HostsChanged += PushHosts;
        ws.Saved += ShowMeta;
        Palette.Changed += _ => PushTheme();

        BuildToolbar();
        BuildOutline();
        BuildCommands();

        AttachedToVisualTree += (_, _) =>
        {
            if (!_navigated)
            {
                _navigated = true;
                _web.NavigateToString(PageHtml(), new Uri("https://ducknote.invalid/"));
            }
        };
    }

    private bool _navigated;

    /// <summary>Un clic nell'indice o un foglio nuovo: la finestra deve mostrare questa pagina.</summary>
    public event Action? Wanted;

    /// <summary>Un tasto della finestra premuto mentre si scrive (F9, Ctrl+L...): lo gestisce la finestra.</summary>
    public event Action<string>? WindowKey;

    /// <summary>Serve l'indice dei fogli (la ricerca da tastiera): se la sidebar e' nascosta deve mostrarsi.</summary>
    public event Action? SidebarWanted;

    public void OnShown()
    {
        _front = true;

        if (_ready)
        {
            // l'attesa si cambia nelle Impostazioni: torna aggiornata a ogni ritorno sulla nota
            Call(string.Create(CultureInfo.InvariantCulture, $"dn.settle({_ws.Settings.HostDebounceMs})"));
        }

        RefreshOutline();
        FocusText();
    }

    public void OnHidden() => _front = false;

    /// <summary>Il cursore nel testo.</summary>
    public void FocusText()
    {
        GiveKeyboard();
        Call("dn.focus()");
    }

    /// <summary>
    /// La tastiera al WebView, davvero: il WebView passa il fuoco al controllo nativo (WKWebView,
    /// WebKitGTK) solo quando lo riceve. Se per Avalonia ce l'ha gia' (un clic su un pulsante che
    /// non prende il fuoco lo lascia li') non succederebbe nulla: prima lo si toglie.
    /// </summary>
    private void GiveKeyboard()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager is { } focus && ReferenceEquals(focus.GetFocusedElement(), _web))
        {
            // Avalonia 12 non ha ClearFocus: il fuoco a nessuno si chiede cosi'
            focus.Focus(null!, NavigationMethod.Unspecified, KeyModifiers.None);
        }

        _web.Focus();
        NativeFocus.Give(_web);
    }

    // ------------------------------------------------------------------ la pagina dell'editor

    private static string PageHtml()
    {
        string Resource(string name)
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            return stream is null ? string.Empty : new StreamReader(stream).ReadToEnd();
        }

        string art = string.Empty;

        try
        {
            using Stream image = AssetLoader.Open(new Uri("avares://DuckNote/Assets/art/empty_note.png"));
            using MemoryStream copy = new();
            image.CopyTo(copy);
            art = "data:image/png;base64," + Convert.ToBase64String(copy.ToArray());
        }
        catch (Exception problem) when (problem is IOException or FileNotFoundException)
        {
            // l'invito della pagina vuota resta senza disegno
        }

        return "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
               "<style>" + Resource("editor.css") + "</style>" +
               $"<style>:root{{--empty-art:url({art})}}</style></head><body>" +
               "<script>" + Resource("editor.js") + "</script></body></html>";
    }

    /// <summary>
    /// Niente della nota resta su disco per colpa del motore web: niente cache, niente archivi
    /// persistenti (la nota cifrata resta cifrata anche li').
    /// </summary>
    private static void OnEnvironment(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;

        switch (e)
        {
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                apple.NonPersistentDataStore = true;
                apple.ApplicationNameForUserAgent = "DuckNote";
                break;

            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.EphemeralDataManager = true;
                gtk.DisableCache = true;
                break;

            case WindowsWebView2EnvironmentRequestedEventArgs edge:
                edge.IsInPrivateModeEnabled = true;
                break;
        }
    }

    // ------------------------------------------------------------------ verso l'editor

    /// <summary>Uno script nell'editor, quando c'e'; gli errori dell'editor non fermano la finestra.</summary>
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
            System.Diagnostics.Debug.WriteLine($"editor: {problem.Message}");
        }
    }

    private async Task<string?> Ask(string script)
    {
        if (!_ready)
        {
            return null;
        }

        try
        {
            return await _web.InvokeScript(script);
        }
        catch (Exception problem) when (problem is InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static string Js(object? value) => JsonSerializer.Serialize(value);

    private void PushTheme() =>
        Call($"dn.theme({Js(Palette.Css())}, {Js(Palette.Effective == "dark")})");

    private void PushZoom() => Call($"dn.zoom({_ws.Settings.EditorZoom})");

    private void PushHosts()
    {
        OnHostsChanged();
        Call($"dn.hosts({Js(_ws.HostStates())}, {Js(_ws.Session.Hidden)})");
    }

    /// <summary>Un foglio nell'editor: documento, titolo, riga di dati, e i byte dei suoi riquadri.</summary>
    private void Load(Sheet? sheet, bool focus = true)
    {
        _sheet = sheet;
        _ws.CurrentSheet = sheet;

        if (sheet is null || !_ready)
        {
            return;
        }

        _loading++;
        string doc = SheetJson.ToJson(sheet.Doc).ToJsonString();
        string options = Js(new Dictionary<string, object?>
        {
            ["title"] = sheet.Name,
            ["meta"] = MetaLine(sheet),
            ["readonly"] = _ws.IsDamaged || _ws.IsLocked,
            ["focus"] = focus && _front,
            ["hosts"] = new { states = _ws.HostStates(), hidden = _ws.Session.Hidden },
            ["settleMs"] = _ws.Settings.HostDebounceMs
        });
        _typing = null;

        if (focus && _front)
        {
            // il fuoco dentro alla pagina non basta: la tastiera deve arrivare al WebView
            GiveKeyboard();
        }

        Call($"dn.load({doc}, {options})");
        PushInsets(sheet);
        RefreshOutline();
        RefreshCommands();
    }

    private void OnNoteReplaced()
    {
        Load(_ws.IsLocked ? null : _ws.Book.Sheets.FirstOrDefault(), focus: false);

        if (_ws.IsLocked)
        {
            Call("dn.clear()");
        }

        RefreshOutline();
        RefreshCommands();
    }

    /// <summary>La nota si chiude a chiave: via tutto dall'editor, subito.</summary>
    public void Forget()
    {
        _sheet = null;
        Call("dn.clear()");
    }

    /// <summary>Il foglio com'e' adesso nell'editor, prima di salvare per chiudere o bloccare.</summary>
    public async Task Flush()
    {
        if (_sheet is null || _ws.IsLocked)
        {
            return;
        }

        if (await Ask("dn.doc()") is { Length: > 2 } raw && _sheet is { } sheet)
        {
            // InvokeScript restituisce il valore come JSON: una stringa JSON dentro una stringa
            string json = raw.StartsWith('"') ? JsonSerializer.Deserialize<string>(raw) ?? string.Empty : raw;

            if (JsonNode.Parse(json) is { } node)
            {
                Take(sheet, node);
            }
        }
    }

    private string MetaLine(Sheet sheet)
    {
        int words = sheet.Doc.Words();
        int place = _ws.Book.IndexOf(sheet) + 1;
        List<string> meta =
        [
            words == 1 ? "1 parola" : string.Create(CultureInfo.CurrentCulture, $"{words} parole"),
            string.Create(CultureInfo.CurrentCulture, $"pagina {place} di {_ws.Book.Sheets.Count}")
        ];

        if (_ws.LastSaved is { } saved)
        {
            meta.Add(string.Create(CultureInfo.CurrentCulture, $"salvata alle {saved:HH:mm}"));
        }

        if (_ws.Settings.EditorZoom != 100)
        {
            meta.Add(string.Create(CultureInfo.CurrentCulture, $"zoom {_ws.Settings.EditorZoom}%"));
        }

        if (_ws.Vault.IsOpen)
        {
            meta.Add("cifrata");
        }

        return string.Join("  ·  ", meta);
    }

    private void ShowMeta()
    {
        if (_sheet is { } sheet)
        {
            Call($"dn.meta({Js(MetaLine(sheet))})");
        }
    }

    // ------------------------------------------------------------------ dall'editor

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

        if (message is null)
        {
            return;
        }

        string? type = message["t"]?.GetValue<string>();
        JsonNode? doc = message["doc"];

        switch (type)
        {
            case "ready":
                _ready = true;
                PushTheme();
                PushZoom();
                Load(_sheet ?? _ws.Book.Sheets.FirstOrDefault(), focus: _front);
                break;

            case "changed" when _sheet is { } sheet && doc is not null:
                _typing = message["typing"] is JsonValue word ? word.GetValue<string>() : null;
                Take(sheet, doc);
                break;

            case "settled":
                // la parola sotto al cursore e' diventata un indirizzo: in tabella e al controllo
                _typing = null;
                _sync.Stop();
                _ws.SyncHosts();
                break;

            case "state":
                ShowState(message);
                break;

            case "title" when _sheet is { } named:
                Rename(named, message["text"]?.GetValue<string>() ?? string.Empty);
                break;

            case "open":
                Open(message["href"]?.GetValue<string>());
                break;

            case "context":
                ShowContext(message);
                break;

            case "askLink":
                AskLink(message["href"]?.GetValue<string>() ?? string.Empty, message["text"]?.GetValue<string>() ?? string.Empty);
                break;

            case "insetEdit":
                EditChart(message["id"]?.GetValue<string>());
                break;

            case "insetBytes":
                KeepDrawing(message["id"]?.GetValue<string>(), message["text"]?.GetValue<string>());
                break;

            case "pasteImage":
                PasteImage(message["dataUrl"]?.GetValue<string>());
                break;

            case "key":
                OnWindowKey(message["key"]?.GetValue<string>() ?? string.Empty);
                break;

            case "activity":
                _ws.Activity();
                break;
        }
    }

    private void Take(Sheet sheet, JsonNode doc)
    {
        if (_ws.IsLocked || _ws.IsDamaged || !ReferenceEquals(sheet, _ws.CurrentSheet))
        {
            return;
        }

        SheetDoc next = SheetJson.FromJson(doc);
        _ws.Update(sheet, next);

        // gli indirizzi si allineano a riposo, i titoli dell'indice subito
        _sync.Stop();
        _sync.Start();
        RefreshOutline();
        ShowMeta();
    }

    private void Rename(Sheet sheet, string title)
    {
        string wanted = DuckNote.Notes.Notebook.PageIds.Clean(title);

        if (wanted == sheet.Name)
        {
            return;
        }

        sheet.Name = wanted;
        _ws.Touch();
        RefreshOutline();
    }

    private void Open(string? href)
    {
        if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        if (!DuckNote.Notes.Links.WebAddress.Safe(uri))
        {
            _ws.Say($"Collegamento non aperto: lo schema {uri.Scheme} non e' fra quelli ammessi.");
            return;
        }

        _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
    }

    private void OnWindowKey(string key)
    {
        switch (key)
        {
            case "zoomIn":
                Zoom(_ws.Settings.EditorZoom + 10);
                break;
            case "zoomOut":
                Zoom(_ws.Settings.EditorZoom - 10);
                break;
            case "zoomReset":
                Zoom(100);
                break;
            case "searchSheets":
                ShowFilter(true);
                break;
            case "toolbar":
                FocusToolbar();
                break;
            default:
                WindowKey?.Invoke(key);
                break;
        }
    }

    private void Zoom(int percent)
    {
        _ws.Settings.EditorZoom = Math.Clamp(percent, 50, 300);
        _ws.Settings.Save();
        PushZoom();
        ShowMeta();
    }

    /// <summary>Una tabella di risultati portata nella pagina aperta, in fondo.</summary>
    public void InsertTable(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (_sheet is not { } sheet || _ws.IsLocked)
        {
            return;
        }

        List<IReadOnlyList<IReadOnlyList<SheetLine>>> table =
        [
            [.. header.Select(cell => (IReadOnlyList<SheetLine>)[SheetLine.Of(LineKind.Text, cell)])],
            .. rows.Select(row => (IReadOnlyList<IReadOnlyList<SheetLine>>)[.. header.Select((_, i) => (IReadOnlyList<SheetLine>)[SheetLine.Of(LineKind.Text, i < row.Count ? row[i] : string.Empty)])])
        ];

        SheetDoc doc = new();
        doc.Blocks.AddRange(sheet.Doc.Blocks);
        doc.Blocks.Add(new SheetTable(table));
        doc.Blocks.Add(SheetLine.Empty());
        _ws.Update(sheet, doc);
        Load(sheet);
        _ws.Say(string.Create(CultureInfo.CurrentCulture, $"Tabella di {rows.Count} host aggiunta alla pagina \"{sheet.Title}\"."));
    }

    private static Control Label(string text, string token = "LabelSecondary", double size = 12)
    {
        TextBlock block = new() { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(token));
        return block;
    }
}

/// <summary>Una pagina della finestra: sa quando e' in primo piano.</summary>
public interface IDuckPage
{
    void OnShown();

    void OnHidden();
}
