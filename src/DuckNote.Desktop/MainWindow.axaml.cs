using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DuckNote.App.Storage;
using DuckNote.App.Vault;
using DuckNote.Desktop.Pages;
using DuckNote.Desktop.Theme;
using DuckNote.Desktop.Ui;

namespace DuckNote.Desktop;

/// <summary>
/// La finestra: sidebar, fascia del titolo, pagine, blocco. Le pagine sono create una volta e
/// restano vive (la nota e' un WebView: ricrearlo vorrebbe dire ricaricare l'editor); si
/// mostra solo quella in primo piano.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double SideWidth = 266;

    private readonly Workspace _ws;
    private readonly NotesPage _notes;
    private readonly NetworkPage _network;
    private readonly SettingsPage _settings;
    private readonly Notices _notices;
    private readonly Dictionary<string, Control> _pages;
    private readonly StackPanel _lockedTitle;

    private string _current = string.Empty;
    private string _beforeSettings = "notes";
    private bool _sidebarShown = true;
    private bool _closing;

    public MainWindow(Workspace ws)
    {
        _ws = ws;
        InitializeComponent();
        Chrome();

        _notes = new NotesPage(ws);
        _network = new NetworkPage(ws);
        _settings = new SettingsPage(ws, () => _notes.Flush(), LockNow, () => Navigate("notes"));
        _pages = new Dictionary<string, Control>(StringComparer.Ordinal)
        {
            ["notes"] = _notes,
            ["network"] = _network,
            ["settings"] = _settings
        };

        foreach (Control page in _pages.Values)
        {
            page.IsVisible = false;
            PageHost.Children.Add(page);
        }

        NavigatorHost.Content = _notes.Navigator;
        CommandsHost.Content = _notes.Commands;
        ToolbarHost.Content = _notes.Toolbar;

        _notices = new Notices((Panel)Content!, PageHost);
        _lockedTitle = LockedTitle();
        ((Panel)Content!).Children.Add(_lockedTitle);

        Wire();
        Lock.Attach(ws);

        Version? version = typeof(MainWindow).Assembly.GetName().Version;
        Version.Text = version is null ? string.Empty : $"v{version.ToString(3)}";
        ToolTip.SetTip(Version, $"DuckNote {Version.Text}");

        Palette.Changed += _ => ShowTheme();
        ShowTheme();
        ShowUser();
        AppMenu();

        Opened += (_, _) => Start();
        Closing += OnClosing;
    }

    // ------------------------------------------------------------------ la cornice della finestra

    /// <summary>
    /// Sul Mac i semafori sono quelli del sistema, sopra al contenuto; su Windows i pulsanti del
    /// sistema stanno a destra; su Linux DuckNote disegna i suoi.
    /// </summary>
    private void Chrome()
    {
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 46;
            Lights.IsVisible = false;
            TitleLeft.Margin = new Thickness(78, 0, 0, 0);
        }
        else if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 46;
            Lights.IsVisible = false;
            TitleLeft.Margin = new Thickness(12, 0, 0, 0);
            TitleRight.Margin = new Thickness(0, 0, 150, 0);
        }
        else
        {
            WindowDecorations = WindowDecorations.None;
        }
    }

    private void OnTitleDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button && e.ClickCount == 1)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleDouble(object? sender, TappedEventArgs e) => OnMaximize(sender, new RoutedEventArgs());

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Prima di chiudere: il foglio com'e' nell'editor va salvato.</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;
        _notices.Close();
        await _notes.Flush();
        Close();
    }

    // ------------------------------------------------------------------ avvio

    private void Wire()
    {
        _settings.Changed += ShowMonitor;
        _ws.Status += message => Dispatcher.UIThread.Post(() => _notices.Status(message));
        _ws.Locked += () => Dispatcher.UIThread.Post(ShowLock);
        _ws.VaultChanged += () => Dispatcher.UIThread.Post(() =>
        {
            ShowVault();
            ShowSaved();
        });
        _ws.HostsChanged += () =>
        {
            ShowMonitor();
            ShowHosts();
        };
        _ws.Session.Finished += message =>
        {
            if (!_ws.ScanIsAutomatic)
            {
                Dispatcher.UIThread.Post(() => Announce(message));
            }
        };
        _ws.HostRequested += host => Dispatcher.UIThread.Post(() =>
        {
            Navigate("network");
            _network.Reveal(host);
        });
        _ws.TableToNote += (header, rows) => Dispatcher.UIThread.Post(() =>
        {
            Navigate("notes");
            _notes.InsertTable(header, rows);
        });
        _ws.Unsaved += () => Dispatcher.UIThread.Post(ShowSaved);
        _ws.Saved += () => Dispatcher.UIThread.Post(ShowSaved);

        _notes.Wanted += () =>
        {
            if (_current != "notes")
            {
                Navigate("notes");
            }
        };
        _notes.WindowKey += OnWindowKey;
        _notes.SidebarWanted += () => SetSidebar(true);

        Lock.Opened += () =>
        {
            _lockedTitle.IsVisible = false;
            ShowVault();
            Navigate("notes");
            _notices.Status($"Riaperta alle {DateTime.Now:HH:mm}.");
        };

        KeyDown += OnKey;
        AddHandler(PointerPressedEvent, (_, _) => _ws.Activity(), RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, (_, _) => _ws.Activity(), RoutingStrategies.Tunnel);
    }

    private void Start()
    {
        _ws.Start();
        ShowHosts();
        ShowMonitor();
        ShowSaved();
        ShowVault();
        SetSidebar(!_ws.Settings.SidebarHidden, animate: false);

        if (_ws.IsLocked)
        {
            ShowLock();
            return;
        }

        Navigate("notes");

        if (!VaultSession.Exists && !_ws.Settings.SecurityPrompted)
        {
            // il primo avvio propone la cifratura: "Piu' tardi" la rimanda per sempre
            _ws.Settings.SecurityPrompted = true;
            _ws.Settings.Save();
            OpenVault();
        }
    }

    // ------------------------------------------------------------------ navigazione

    public void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out Control? page) || _ws.IsLocked)
        {
            return;
        }

        if (key == "settings" && _current is { Length: > 0 } and not "settings")
        {
            _beforeSettings = _current;
        }

        if (_current == key)
        {
            (page as IDuckPage)?.OnShown();
            return;
        }

        if (_pages.GetValueOrDefault(_current) is { } leaving)
        {
            leaving.IsVisible = false;
            (leaving as IDuckPage)?.OnHidden();
        }

        _current = key;
        Title = key switch
        {
            "network" => "DuckNote - Rete",
            "settings" => "DuckNote - Impostazioni",
            _ => "DuckNote"
        };

        // la nota e' un WebView: si mostra e basta (un controllo nativo non sfuma)
        if (key == "notes")
        {
            page.IsVisible = true;
        }
        else
        {
            Motion.Show(page, 10, 260);
        }

        NavNetwork.Classes.Set("active", key == "network");
        ShowToolbar(key == "notes");
        (page as IDuckPage)?.OnShown();
    }

    private void ShowToolbar(bool on)
    {
        if (on)
        {
            Motion.Show(ToolbarHost, -6, 240);
        }
        else
        {
            Motion.Hide(ToolbarHost, -6, 160);
        }
    }

    private void OnNetwork(object? sender, RoutedEventArgs e) => Navigate("network");

    private void OnSettings(object? sender, RoutedEventArgs e) =>
        Navigate(_current == "settings" ? _beforeSettings : "settings");

    private void OpenVault()
    {
        Navigate("settings");
        _settings.ShowVault();
    }

    // ------------------------------------------------------------------ la sidebar

    private void OnSidebarToggle(object? sender, RoutedEventArgs e) => SetSidebar(!_sidebarShown);

    /// <summary>
    /// La sidebar entra o esce scorrendo, e il foglio prende tutto il posto (o lo restituisce).
    /// La scelta resta anche al prossimo avvio.
    /// </summary>
    private void SetSidebar(bool shown, bool animate = true)
    {
        if (_ws.IsLocked && shown == _sidebarShown)
        {
            return;
        }

        _sidebarShown = shown;
        double from = Shell.ColumnDefinitions[0].ActualWidth;
        double to = shown ? SideWidth : 0;
        double gapTo = shown ? 0 : TitleLeft.Bounds.Width + TitleLeft.Margin.Left + 6;
        double gapFrom = TitleGap.Width;

        Motion.Tween((this, "sidebar"), 0, 1, animate ? 280 : 0, t =>
        {
            Shell.ColumnDefinitions[0].Width = new GridLength(from + (to - from) * t);
            TitleGap.Width = Math.Max(0, gapFrom + (gapTo - gapFrom) * t);
        });

        ToolTip.SetTip(BtnSidebar, shown
            ? $"Nascondi la barra laterale: tutto lo spazio al foglio ({(OperatingSystem.IsMacOS() ? "⌘\\" : "F9")})"
            : $"Mostra la barra laterale ({(OperatingSystem.IsMacOS() ? "⌘\\" : "F9")})");

        if (_ws.Settings.SidebarHidden == shown)
        {
            _ws.Settings.SidebarHidden = !shown;
            _ws.Settings.Save();
        }
    }

    // ------------------------------------------------------------------ tasti

    private void OnKey(object? sender, KeyEventArgs e)
    {
        _ws.Activity();
        _notices.Typing();
        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        if (e.Key == Key.F9 || (e.Key == Key.OemPipe && e.KeyModifiers == command) || (e.Key == Key.Oem5 && e.KeyModifiers == command))
        {
            OnWindowKey("sidebar");
            e.Handled = true;
        }
        else if (e.Key == Key.L && e.KeyModifiers == command)
        {
            OnWindowKey("lock");
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == (command | KeyModifiers.Shift))
        {
            Navigate("notes");
            _notes.ShowFilter(true);
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma && e.KeyModifiers == command)
        {
            OnWindowKey("settings");
            e.Handled = true;
        }
    }

    /// <summary>I tasti della finestra, anche quando arrivano dall'editor della nota.</summary>
    private void OnWindowKey(string key)
    {
        _notices.Typing();

        switch (key)
        {
            case "sidebar" when !_ws.IsLocked:
                SetSidebar(!_sidebarShown);
                break;
            case "lock" when _ws.Vault.IsOpen:
                _ = LockNow();
                break;
            case "settings" when !_ws.IsLocked:
                OnSettings(null, new RoutedEventArgs());
                break;
        }
    }

    /// <summary>Blocca: il foglio com'e' nell'editor si salva, poi la cassaforte si chiude.</summary>
    private async Task LockNow()
    {
        await _notes.Flush();
        _ws.Lock("a mano");
    }

    private void AppMenu()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        NativeMenu menu = new();
        NativeMenuItem view = new("Vista") { Menu = new NativeMenu() };
        NativeMenuItem sidebar = new("Mostra o nascondi la barra laterale") { Gesture = new KeyGesture(Key.Oem5, KeyModifiers.Meta) };
        sidebar.Click += (_, _) => OnWindowKey("sidebar");
        view.Menu!.Add(sidebar);

        NativeMenuItem note = new("Nota") { Menu = new NativeMenu() };
        NativeMenuItem lockItem = new("Blocca la nota") { Gesture = new KeyGesture(Key.L, KeyModifiers.Meta) };
        lockItem.Click += (_, _) => OnWindowKey("lock");
        NativeMenuItem settings = new("Impostazioni...") { Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta) };
        settings.Click += (_, _) => OnWindowKey("settings");
        note.Menu!.Add(lockItem);
        note.Menu.Add(settings);

        // Modifica: senza, nel foglio (un WKWebView) Cmd+C, Cmd+V, Cmd+X e Cmd+A non farebbero
        // nulla, perche' sul Mac quei tasti passano dal menu. Ogni voce va al campo di testo che
        // ha il fuoco, oppure al foglio.
        NativeMenuItem edit = new("Modifica") { Menu = new NativeMenu() };

        void Add(string header, Key key, KeyModifiers modifiers, string action)
        {
            NativeMenuItem item = new(header) { Gesture = new KeyGesture(key, modifiers) };
            item.Click += (_, _) => OnEdit(action);
            edit.Menu!.Add(item);
        }

        Add("Annulla", Key.Z, KeyModifiers.Meta, "undo");
        Add("Ripeti", Key.Z, KeyModifiers.Meta | KeyModifiers.Shift, "redo");
        edit.Menu!.Add(new NativeMenuItemSeparator());
        Add("Taglia", Key.X, KeyModifiers.Meta, "cut");
        Add("Copia", Key.C, KeyModifiers.Meta, "copy");
        Add("Incolla", Key.V, KeyModifiers.Meta, "paste");
        Add("Incolla solo testo", Key.V, KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt, "pastePlain");
        Add("Seleziona tutto", Key.A, KeyModifiers.Meta, "selectAll");

        menu.Add(note);
        menu.Add(edit);
        menu.Add(view);
        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>Una voce del menu Modifica: al campo di testo col fuoco, o al foglio della nota.</summary>
    private void OnEdit(string action)
    {
        switch (FocusManager?.GetFocusedElement())
        {
            case TextBox box:
                switch (action)
                {
                    case "undo": box.Undo(); break;
                    case "redo": box.Redo(); break;
                    case "cut": box.Cut(); break;
                    case "copy": box.Copy(); break;
                    case "paste" or "pastePlain": box.Paste(); break;
                    case "selectAll": box.SelectAll(); break;
                }

                return;

            case SelectableTextBlock text when action == "copy":
                text.Copy();
                return;

            case SelectableTextBlock text when action == "selectAll":
                text.SelectAll();
                return;
        }

        if (_current == "notes" && !_ws.IsLocked)
        {
            _notes.Edit(action);
        }
    }

    // ------------------------------------------------------------------ blocco

    private void ShowLock()
    {
        _notes.Forget();

        foreach (Control page in _pages.Values)
        {
            page.IsVisible = false;
        }

        _current = string.Empty;
        _notices.Close();
        Lock.Present();
        _lockedTitle.IsVisible = true;
        ShowVault();
        ShowSaved();
    }

    /// <summary>A nota bloccata restano solo la versione e giorno/notte, sopra alla schermata di sblocco.</summary>
    private StackPanel LockedTitle()
    {
        TextBlock version = new() { Classes = { "caption" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        version.Bind(TextBlock.TextProperty, Version.GetObservable(TextBlock.TextProperty));
        Glyph glyph = new() { Kind = "moon" };
        Button theme = new() { Width = 30, Content = glyph, Theme = Dialog("Tool") };
        theme.Click += (_, _) => Palette.Toggle();
        Palette.Changed += _ => glyph.Kind = Palette.Effective == "dark" ? "sun" : "moon";

        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Height = 46,
            Margin = TitleRight.Margin,
            IsVisible = false,
            ZIndex = 20,
            Children = { version, theme }
        };
    }

    private static Avalonia.Styling.ControlTheme? Dialog(string key) => Themes.Of(key);

    // ------------------------------------------------------------------ utente e cassaforte

    private void OnAccount(object? sender, RoutedEventArgs e) => ShowAccountMenu(BtnAccount, withExit: true);

    private void OnLockButton(object? sender, RoutedEventArgs e)
    {
        if (_ws.Vault.IsOpen)
        {
            _ = LockNow();
            return;
        }

        ShowAccountMenu(BtnLock, withExit: false);
    }

    /// <summary>Con la cifratura attiva propone di bloccare la nota, senza propone di impostarla.</summary>
    private void ShowAccountMenu(Control target, bool withExit)
    {
        if (_ws.IsLocked)
        {
            return;
        }

        bool vault = _ws.Vault.IsOpen;
        ContextMenu menu = new();
        MenuItem head = new()
        {
            Header = vault ? "La nota e' cifrata" : "La nota non e' cifrata",
            IsEnabled = false
        };
        menu.Items.Add(head);

        if (vault)
        {
            MenuItem lockNow = new() { Header = OperatingSystem.IsMacOS() ? "Blocca la nota    ⌘L" : "Blocca la nota    Ctrl+L", Icon = new Glyph { Kind = "lock" } };
            lockNow.Click += (_, _) => _ = LockNow();
            menu.Items.Add(lockNow);
        }
        else
        {
            MenuItem setUp = new() { Header = "Imposta la cifratura...", Icon = new Glyph { Kind = "shield" } };
            setUp.Click += (_, _) => OpenVault();
            menu.Items.Add(setUp);
        }

        if (withExit)
        {
            menu.Items.Add(new Separator());
            MenuItem exit = new() { Header = "Esci da DuckNote", Icon = new Glyph { Kind = "exit" } };
            exit.Click += (_, _) => Close();
            menu.Items.Add(exit);
        }

        menu.Open(target);
    }

    private void ShowUser()
    {
        string name = CurrentUser.DisplayName;
        AccountName.Text = name;
        AccountSub.Text = CurrentUser.Where;

        try
        {
            Bitmap duck = new(AssetLoader.Open(new Uri($"avares://DuckNote/Assets/avatars/duck{CurrentUser.DuckIndex(name):00}.png")));
            AccountPhoto.Fill = new ImageBrush(duck) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception problem) when (problem is IOException or FileNotFoundException)
        {
            AccountPhoto.Fill = Palette.Brush("AccentSoft");
        }
    }

    private void ShowVault()
    {
        (string token, string glyph, string text, string tip) = _ws.Vault.IsOpen
            ? ("Green", "lock", "Nota cifrata, aperta", "Clic: blocca la nota o esci.")
            : _ws.IsLocked
                ? ("Orange", "lock", "Nota chiusa a chiave", "Serve la password per aprirla")
                : ("LabelQuaternary", "unlock", "Nota in chiaro", "La nota non e' cifrata. Clic: imposta la cifratura o esci.");

        AccountBadge.Bind(Border.BackgroundProperty, AccountBadge.GetResourceObservable(token));
        AccountBadgeGlyph.Kind = glyph;
        AccountVault.Text = text;
        AccountVault.Bind(TextBlock.ForegroundProperty, AccountVault.GetResourceObservable(token == "LabelQuaternary" ? "LabelTertiary" : token));
        ToolTip.SetTip(BtnAccount, tip);

        LockGlyph.Kind = _ws.Vault.IsOpen || _ws.IsLocked ? "lock" : "unlock";
        LockGlyph.Bind(Glyph.ForegroundProperty, LockGlyph.GetResourceObservable(_ws.Vault.IsOpen ? "Orange" : "LabelSecondary"));
        ToolTip.SetTip(BtnLock, _ws.Vault.IsOpen
            ? (OperatingSystem.IsMacOS() ? "Blocca la nota (⌘L)" : "Blocca la nota (Ctrl+L)")
            : "Cifratura non attiva: clic per impostarla");
    }

    private void ShowTheme() => ThemeGlyph.Kind = Palette.Effective == "dark" ? "sun" : "moon";

    private void OnTheme(object? sender, RoutedEventArgs e)
    {
        Palette.Toggle();
        _ws.Settings.Theme = Palette.Effective;
        _ws.Settings.FollowSystemTheme = false;
        _ws.Settings.Save();
    }

    /// <summary>Il pallino accanto alla versione: arancio con modifiche da scrivere, verde quando sono su disco.</summary>
    private void ShowSaved()
    {
        if (_ws.IsLocked)
        {
            SaveDot.IsVisible = false;
            return;
        }

        bool pending = _ws.HasUnsaved;
        SaveDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, SaveDot.GetResourceObservable(pending ? "Orange" : "Green"));
        ToolTip.SetTip(SaveDot, pending
            ? "Modifiche da salvare: si scrivono da sole appena ti fermi"
            : _ws.LastSaved is { } at ? $"Nota salvata alle {at:HH:mm:ss}" : "Nota salvata");

        if (!SaveDot.IsVisible)
        {
            SaveDot.IsVisible = true;
            Motion.Tween((SaveDot, "fade"), 0.25, 1, 320, t => SaveDot.Opacity = t);
        }
    }

    // ------------------------------------------------------------------ host e monitoraggio

    private void ShowHosts()
    {
        int down = _ws.Session.DownCount, up = _ws.Session.UpCount;
        BadgeDown.IsVisible = down > 0;
        BadgeDownText.Text = down > 999 ? "999+" : down.ToString(CultureInfo.CurrentCulture);
        ToolTip.SetTip(BadgeDown, down == 1 ? "1 host spento" : $"{down} host spenti");
        BadgeUp.IsVisible = up > 0;
        BadgeUpText.Text = up > 999 ? "999+" : up.ToString(CultureInfo.CurrentCulture);
        ToolTip.SetTip(BadgeUp, up == 1 ? "1 host attivo" : $"{up} host attivi");
    }

    /// <summary>La scheda in fondo alla sidebar: monitoraggio a riposo, oppure la scansione in corso.</summary>
    private void ShowMonitor()
    {
        var session = _ws.Session;

        if (session.IsRunning)
        {
            Card("Analisi in corso", session.Message, session.Progress, "Blue", string.Create(CultureInfo.CurrentCulture, $"{session.Progress:P0}"));
            return;
        }

        if (_ws.IsLocked)
        {
            Card("Monitoraggio", "Fermo finche' la nota e' chiusa a chiave.", null, "Gray", string.Empty);
            return;
        }

        int hosts = _ws.NoteHostCount;
        Card(
            "Monitoraggio",
            _ws.Settings.MonitorEnabled
                ? string.Create(CultureInfo.CurrentCulture, $"{hosts} host della nota, ogni {_ws.Settings.MonitorIntervalSec} s. {session.UpCount} attivi, {session.DownCount} spenti.")
                : "Spento: gli host si controllano solo a mano.",
            null,
            _ws.Settings.MonitorEnabled ? (session.DownCount > 0 ? "Orange" : "Green") : "Gray",
            _ws.Settings.MonitorEnabled ? string.Create(CultureInfo.CurrentCulture, $"{_ws.Settings.MonitorIntervalSec}s") : "fermo");
    }

    private void Card(string title, string text, double? progress, string dot, string right)
    {
        CardTitle.Text = title;
        CardText.Text = text;
        CardRight.Text = right;
        CardProgress.IsVisible = progress is not null;
        CardProgress.Value = progress ?? 0;
        CardDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, CardDot.GetResourceObservable(dot));
    }

    private void Announce(string message)
    {
        if (message.StartsWith("Analisi interrotta", StringComparison.Ordinal))
        {
            _notices.Error("Analisi non riuscita", message);
        }
        else if (message.StartsWith("Fermata", StringComparison.Ordinal))
        {
            _notices.Info("Analisi fermata", message);
        }
        else if (!message.StartsWith("Indica", StringComparison.Ordinal) && !message.StartsWith("Niente", StringComparison.Ordinal))
        {
            _notices.Success("Analisi finita", message);
        }
    }
}
