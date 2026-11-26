using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using DuckNote.Desktop.Notes;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Charts;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Sheets;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// I riquadri (immagini, grafici, disegni), il menu del tasto destro, i collegamenti e il
/// pulsante che controlla gli host dei fogli.
/// </summary>
public sealed partial class NotesPage
{
    // ------------------------------------------------------------------ riquadri

    /// <summary>I byte dei riquadri del foglio, come li vuole l'editor (immagini come data URL).</summary>
    private void PushInsets(Sheet sheet)
    {
        foreach (InsetAddress inset in sheet.Doc.Insets())
        {
            if (_ws.Book.Insets.TryGetValue(inset.Entry, out byte[]? bytes))
            {
                Call($"dn.insetData({Js(inset.Id)}, {Js(InsetValue(inset.Kind, bytes))})");
            }
        }
    }

    private static object InsetValue(InsetKind kind, byte[] bytes) => kind switch
    {
        InsetKind.Picture => new { src = $"data:{ImageType(bytes)};base64,{Convert.ToBase64String(bytes)}" },
        // i disegni della versione WPF sono tratti ISF, non testo: l'editor lo dice e li lascia stare
        InsetKind.Drawing => new { text = LooksLikeText(bytes) ? Encoding.UTF8.GetString(bytes) : "ISF" },
        _ => new { text = Encoding.UTF8.GetString(bytes) }
    };

    private static bool LooksLikeText(byte[] bytes) => bytes.Length > 0 && bytes[0] == (byte)'{';

    private static string ImageType(byte[] bytes) => bytes switch
    {
        [0x89, 0x50, ..] => "image/png",
        [0xFF, 0xD8, ..] => "image/jpeg",
        [0x47, 0x49, ..] => "image/gif",
        [0x42, 0x4D, ..] => "image/bmp",
        [0x52, 0x49, 0x46, 0x46, ..] => "image/webp",
        _ => "image/png"
    };

    private void Insert(InsetKind kind, byte[] bytes, double width = InsetAddress.BornWidth, double height = InsetAddress.BornHeight)
    {
        if (_sheet is null || ReadOnly)
        {
            return;
        }

        InsetAddress address = InsetAddress.Fresh(kind) with
        {
            Width = Math.Clamp(width, InsetAddress.LeastWidth, InsetAddress.MostWidth),
            Height = Math.Clamp(height, InsetAddress.LeastHeight, InsetAddress.MostHeight)
        };

        _ws.Book.Insets[address.Entry] = bytes;
        Call($"dn.insetData({Js(address.Id)}, {Js(InsetValue(kind, bytes))})");
        Call($"dn.insertInset({Js(new { id = address.Id, kind = SheetJson.KindName(kind), width = Math.Round(address.Width), height = Math.Round(address.Height) })})");
        _ws.Say($"{address.Title} aggiunto: scrivi di fianco, e trascina i bordi per la misura.");
    }

    private void NewDrawing() => Insert(InsetKind.Drawing, Encoding.UTF8.GetBytes("{\"v\":1,\"strokes\":[]}"));

    private async void NewChart()
    {
        if (await Views.ChartDialog.Ask(TopLevel.GetTopLevel(this) as Window, ChartSpec.Example) is { } written)
        {
            Insert(InsetKind.Chart, Encoding.UTF8.GetBytes(written));
        }
    }

    private async void EditChart(string? id)
    {
        if (id is null || _ws.Book.Insets.FirstOrDefault(pair => pair.Key.StartsWith($"riquadri/{id}.", StringComparison.Ordinal)) is not { Key: not null } found)
        {
            return;
        }

        string current = Encoding.UTF8.GetString(found.Value);

        if (await Views.ChartDialog.Ask(TopLevel.GetTopLevel(this) as Window, current) is { } written && written != current)
        {
            _ws.Book.Insets[found.Key] = Encoding.UTF8.GetBytes(written);
            Call($"dn.insetData({Js(id)}, {Js(new { text = written })})");
            _ws.Touch();
        }
    }

    private void KeepDrawing(string? id, string? text)
    {
        if (id is null || text is null || _sheet is null)
        {
            return;
        }

        string entry = new InsetAddress(id, InsetKind.Drawing, InsetAddress.BornHeight, InsetAddress.BornWidth).Entry;
        _ws.Book.Insets[entry] = Encoding.UTF8.GetBytes(text);
        _ws.Touch();
    }

    private void PasteImage(string? dataUrl)
    {
        if (dataUrl is null || dataUrl.IndexOf(',', StringComparison.Ordinal) is not (var comma and > 0))
        {
            return;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            AddPicture(bytes);
        }
        catch (FormatException)
        {
            _ws.Say("Immagine non letta: i dati incollati non sono un'immagine.");
        }
    }

    private async void PickPicture()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scegli un'immagine",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }]
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy);
            AddPicture(copy.ToArray());
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            _ws.Say("Immagine non letta: il file non e' accessibile.");
        }
    }

    /// <summary>Un'immagine nel foglio, grande quanto serve per vederla bene ma non piu' larga della colonna.</summary>
    private void AddPicture(byte[] bytes)
    {
        double width = 360, height = 230;

        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using Bitmap bitmap = new(stream);
            double scale = Math.Min(1, 420.0 / Math.Max(1, bitmap.PixelSize.Width));
            width = bitmap.PixelSize.Width * scale;
            height = bitmap.PixelSize.Height * scale + 27;
        }
        catch (Exception problem) when (problem is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            _ws.Say("Questo formato d'immagine non si apre.");
            return;
        }

        Insert(InsetKind.Picture, bytes, width, height);
    }

    // ------------------------------------------------------------------ il destro

    /// <summary>
    /// Il menu del tasto destro sa cosa sta guardando: su un indirizzo offre di analizzarlo, di
    /// vederne la scheda, di metterlo da parte e di copiarlo; su una tabella, un codice, una
    /// citazione o una separazione di eliminarli; e sempre taglia, copia, incolla.
    /// </summary>
    private void ShowContext(JsonNode message)
    {
        string? host = message["host"] is JsonValue h ? h.GetValue<string>() : null;
        string? link = message["link"] is JsonValue l ? l.GetValue<string>() : null;
        JsonNode? block = message["block"];
        double x = message["x"]?.GetValue<double>() ?? 0;
        double y = message["y"]?.GetValue<double>() ?? 0;

        ContextMenu menu = new();

        if (host is not null)
        {
            menu.Items.Add(Item($"Analizza {host}", "refresh", "Green", async () => await _ws.Analyse(host)));
            menu.Items.Add(Item($"Scheda di {host}", "info", null, () => _ws.RequestHost(host)));
            menu.Items.Add(Item($"Non analizzare piu' {host}", "hide", null, () => _ws.Hide(host)));
            menu.Items.Add(Item($"Copia {host}", "copy", null, () => Copy(host)));
            menu.Items.Add(new Separator());
        }

        if (_ws.Session.Hidden.Count > 0)
        {
            MenuItem hidden = new() { Header = "Host messi da parte", Icon = Tinted("show", "Green") };

            foreach (string aside in _ws.Session.Hidden.Order(StringComparer.OrdinalIgnoreCase))
            {
                hidden.Items.Add(Item($"Rimetti {aside}", "show", null, () => _ws.Show(aside)));
            }

            menu.Items.Add(hidden);
            menu.Items.Add(new Separator());
        }

        if (link is not null)
        {
            menu.Items.Add(Item("Apri il collegamento", "open", "Blue", () => Open(link)));
            menu.Items.Add(Item("Modifica il collegamento", "link", null, () => Command("askLink")));
            menu.Items.Add(new Separator());
        }

        if (block?["kind"] is JsonValue kind && block["pos"] is JsonValue pos)
        {
            string what = kind.GetValue<string>() switch
            {
                "table" => "la tabella",
                "code" => "il blocco di codice",
                "quote" => "la citazione",
                _ => "la separazione"
            };
            int at = pos.GetValue<int>();
            menu.Items.Add(Item($"Elimina {what}", "trash", "Red", () =>
            {
                Call($"dn.cmd('dropBlock', {at})");
                _ws.Say($"{char.ToUpperInvariant(what[0])}{what[1..]} e' stata eliminata. Ctrl+Z la rimette.");
            }));
            menu.Items.Add(new Separator());
        }

        bool selection = message["selection"] is JsonValue s && s.GetValue<bool>();
        menu.Items.Add(Item("Taglia", null, null, () => Edit("cut"), selection && !ReadOnly));
        menu.Items.Add(Item("Copia", null, null, () => Edit("copy"), selection));
        menu.Items.Add(Item("Incolla", null, null, () => Edit("paste"), !ReadOnly));
        menu.Items.Add(Item("Incolla solo testo", null, null, PastePlain, !ReadOnly));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Seleziona tutto", null, null, () => Call("dn.cmd('selectAll')")));

        // le coordinate della pagina sono gia' in DIP: lo zoom della nota ingrandisce il testo, non la pagina
        menu.PlacementRect = new Rect(x, y, 1, 1);
        menu.Placement = PlacementMode.AnchorAndGravity;
        menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        menu.Open(_web);
    }

    private static MenuItem Item(string header, string? icon, string? tone, Action run, bool enabled = true)
    {
        MenuItem item = new() { Header = header, IsEnabled = enabled };

        if (icon is not null)
        {
            item.Icon = tone is null ? new Glyph { Kind = icon } : Tinted(icon, tone);
        }

        item.Click += (_, _) => run();
        return item;
    }

    private static Glyph Tinted(string icon, string tone)
    {
        Glyph glyph = new() { Kind = icon };
        glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(tone));
        return glyph;
    }

    /// <summary>
    /// Le voci di modifica del menu (Modifica sul Mac, tasto destro ovunque) sul foglio. Copia,
    /// Taglia e Incolla sul Mac vanno al WKWebView come le manderebbe un menu Cocoa: col formato
    /// della nota, e senza i limiti che la pagina ha sugli appunti quando glielo chiede il codice.
    /// </summary>
    public void Edit(string action)
    {
        switch (action)
        {
            case "undo" or "redo" or "selectAll":
                Call($"dn.cmd('{action}')");
                break;

            case "pastePlain":
                PastePlain();
                break;

            case "paste":
                if (!NativeFocus.Edit(_web, "paste:"))
                {
                    PastePlain();
                }

                break;

            case "cut" or "copy":
                if (!NativeFocus.Edit(_web, action + ":"))
                {
                    Call($"document.execCommand('{action}')");
                }

                break;
        }
    }

    private async void Copy(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            _ws.Say($"{text} copiato.");
        }
    }

    private async void PastePlain()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { } text)
        {
            Call($"dn.cmd('paste', {Js(text)})");
        }
    }

    // ------------------------------------------------------------------ collegamenti

    private async void AskLink(string address, string shown)
    {
        if (await Views.LinkDialog.Ask(TopLevel.GetTopLevel(this) as Window, shown, address) is not { } choice)
        {
            FocusText();
            return;
        }

        if (choice.Removed)
        {
            Call("dn.cmd('link', {href: ''})");
            return;
        }

        Call($"dn.cmd('link', {Js(new { href = choice.Address, text = choice.Text })})");
    }

    // ------------------------------------------------------------------ il pulsante degli host

    private readonly Button _commands = new() { Height = 22, MinWidth = 22, Padding = new Thickness(4, 0) };
    private readonly Border _hostsChip = new() { Classes = { "badge" }, Margin = new Thickness(5, 0, 0, 0) };
    private readonly TextBlock _hostsChipText = new();

    /// <summary>Il pulsante che controlla gli host dei fogli: sta nella scheda del monitoraggio.</summary>
    public Control Commands => _commands;

    private void BuildCommands()
    {
        _commands.Theme = Find("Tool");
        _hostsChip.Child = _hostsChipText;
        _commands.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new Glyph { Kind = "refresh", Width = 11, Height = 11 }, _hostsChip }
        };
        _commands.Click += async (_, _) =>
        {
            _commands.IsEnabled = false;

            try
            {
                await _ws.ScanNote();
            }
            finally
            {
                RefreshCommands();
            }
        };
        RefreshCommands();
    }

    private void OnHostsChanged() => RefreshCommands();

    private void RefreshCommands()
    {
        IReadOnlyList<string> hosts = _ws.IsLocked ? [] : _ws.NoteHosts();
        int up = hosts.Count(host => _ws.StateOf(host) == true);
        int down = hosts.Count(host => _ws.StateOf(host) == false);
        string tone = hosts.Count == 0 ? "Gray" : down > 0 ? "Orange" : up == hosts.Count ? "Green" : "Blue";

        _hostsChip.Bind(Border.BackgroundProperty, _hostsChip.GetResourceObservable(tone + "Soft"));
        _hostsChipText.Bind(TextBlock.ForegroundProperty, _hostsChipText.GetResourceObservable(tone == "Gray" ? "LabelTertiary" : tone));
        _hostsChipText.Text = string.Create(CultureInfo.CurrentCulture, $"{up}/{hosts.Count}");
        ToolTip.SetTip(_commands, hosts.Count == 0
            ? "Nessun indirizzo scritto nei fogli"
            : string.Create(CultureInfo.CurrentCulture, $"{up} attivi su {hosts.Count} scritti nei fogli: clic per controllarli adesso"));
        _commands.IsEnabled = hosts.Count > 0 && !_ws.Session.IsRunning;
    }
}
