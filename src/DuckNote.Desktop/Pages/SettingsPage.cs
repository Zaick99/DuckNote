using System.Globalization;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DuckNote.Desktop.Theme;
using DuckNote.Desktop.Ui;
using DuckNote.Desktop.Views;
using DuckNote.Notes.Settings;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// Le impostazioni a schede, come nella versione WPF: un blocco centrato, largo al massimo 1100,
/// con i gruppi su due colonne. Ogni modifica vale subito: niente tasto Salva.
/// </summary>
public sealed class SettingsPage : UserControl, IDuckPage
{
    private const string IeeeRegistry = "https://standards-oui.ieee.org/oui/oui.txt";

    private static readonly string SystemName =
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : "il sistema";

    private static readonly (string Art, string Title, string Sub)[] Sections =
    [
        ("settings", "Aspetto", $"Tema chiaro, scuro o come {SystemName}."),
        ("note", "Nota", "Quando si salva, e quando una parola scritta diventa un host da controllare."),
        ("network", "Scansione", "Il monitoraggio degli host della nota, il ritmo delle analisi e le porte da provare."),
        ("hosts", "Raccolta", "Cosa chiedere a ogni host: nomi, produttori, servizi, inventario."),
        ("vault", "Cassaforte", string.Empty),
        ("system", "Sistema", "Dove stanno i dati, la verifica dell'eseguibile, la disinstallazione.")
    ];

    private readonly Workspace _ws;
    private readonly VaultPanel _vault;
    private readonly RadioButton[] _tabs;
    private readonly Control[] _panels;
    private readonly Image _art = new() { Width = 52, Height = 52, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _overline = new() { FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 0.6 };
    private readonly TextBlock _sub = new() { TextWrapping = TextWrapping.Wrap };
    private int _section;
    private bool _loading = true;

    // aspetto
    private readonly RadioButton _themeSystem;
    private readonly RadioButton _themeLight;
    private readonly RadioButton _themeDark;

    // nota
    private readonly OnOff _autosave;
    private readonly TextBox _autosaveMs = Kit.Number(90, "1200");
    private readonly TextBox _hostMs = Kit.Number(90, "5000");

    // scansione
    private readonly OnOff _monitor;
    private readonly TextBox _monitorSec = Kit.Number(90, "60");
    private readonly TextBox _ports = new() { Height = 64, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, PlaceholderText = "22, 80, 443", Margin = new Thickness(0, 8, 0, 0), VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(10, 8) };
    private readonly TextBlock _portsInfo = Kit.Note(string.Empty);
    private readonly OnOff _deadHosts;
    private readonly TextBox _threads = Kit.Number(90, "256");
    private readonly TextBox _sockets = Kit.Number(90, "900");
    private readonly TextBox _pingCount = Kit.Number(90, "2");
    private readonly TextBox _pingMs = Kit.Number(90, "800");
    private readonly TextBox _portMs = Kit.Number(90, "500");

    // raccolta
    private readonly OnOff _dns;
    private readonly TextBox _dnsServer = new() { Width = 150, PlaceholderText = "1.1.1.1", Height = 32 };
    private readonly OnOff _netBios;
    private readonly OnOff _mdns;
    private readonly Button _oui = new() { Content = "Aggiorna" };
    private readonly TextBlock _ouiInfo = Kit.Note(string.Empty);
    private readonly OnOff _ssdp;
    private readonly OnOff _banners;
    private readonly OnOff _snmp;
    private readonly TextBox _community = new() { Width = 150, PlaceholderText = "public", Height = 32 };
    private readonly OnOff _shares;
    private readonly OnOff _wmi;

    // sistema
    private readonly TextBlock _dataFolder = Kit.Note(string.Empty);
    private readonly SelectableTextBlock _fingerprint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _fingerprintPath = Kit.Note(string.Empty);

    public SettingsPage(Workspace ws, Func<Task> flush, Func<Task> lockNow, Action backToNotes)
    {
        _ws = ws;
        _vault = new VaultPanel(ws, flush, lockNow, backToNotes);
        _vault.SummaryChanged += () =>
        {
            if (_section == 4)
            {
                _sub.Text = _vault.Summary;
            }
        };

        _themeSystem = Segment("theme", $"Come {SystemName}");
        _themeLight = Segment("theme", "Chiaro");
        _themeDark = Segment("theme", "Scuro");
        foreach (RadioButton theme in new[] { _themeSystem, _themeLight, _themeDark })
        {
            theme.IsCheckedChanged += (_, _) =>
            {
                if (theme.IsChecked == true)
                {
                    OnTheme();
                }
            };
        }

        _autosave = Kit.Switch(false, _ => OnSwitch());
        _monitor = Kit.Switch(false, _ => OnSwitch());
        _deadHosts = Kit.Switch(false, _ => OnSwitch());
        _dns = Kit.Switch(false, _ => OnSwitch());
        _netBios = Kit.Switch(false, _ => OnSwitch());
        _mdns = Kit.Switch(false, _ => OnSwitch());
        _ssdp = Kit.Switch(false, _ => OnSwitch());
        _banners = Kit.Switch(false, _ => OnSwitch());
        _snmp = Kit.Switch(false, _ => OnSwitch());
        _shares = Kit.Switch(false, _ => OnSwitch());
        _wmi = Kit.Switch(false, _ => OnSwitch());

        foreach (TextBox number in new[] { _autosaveMs, _hostMs, _monitorSec, _threads, _sockets, _pingCount, _pingMs, _portMs })
        {
            Confirmed(number, OnNumbers);
        }

        Confirmed(_ports, OnPorts);
        Confirmed(_dnsServer, OnTexts);
        Confirmed(_community, OnTexts);
        _dnsServer.FontFamily = Mono;
        _community.FontFamily = Mono;
        _ports.FontFamily = Mono;
        _fingerprint.FontFamily = Mono;
        _dataFolder.FontFamily = Mono;
        _fingerprintPath.FontFamily = Mono;
        _fingerprint.Bind(TextBlock.ForegroundProperty, _fingerprint.GetResourceObservable("Label"));

        _oui.Theme = Themes.Of("Soft");
        _oui.Click += async (_, _) => await OnOui();

        Button openFolder = new() { Theme = Themes.Of("Soft"), Content = "Apri" };
        openFolder.Click += async (_, _) => await OnOpenFolder();
        Button wipe = new() { Theme = Themes.Of("Danger"), Content = "Cancella tutto..." };
        wipe.Click += async (_, _) => await OnWipe();

        // ------------------------------------------------------------ le schede
        _panels =
        [
            Panel(Kit.Columns(
                Kit.Stack(
                    Kit.Header("Tema", first: true),
                    Kit.Group(Kit.Block("Aspetto", $"Come {SystemName} segue il sistema anche a finestra aperta.", Well(new Thickness(0, 10, 0, 0), _themeSystem, _themeLight, _themeDark)))),
                Kit.Stack(
                    Kit.Header("Scorciatoie", first: true),
                    Kit.Group(
                        Kit.Row("Blocca la nota", null, Keys(Mod + "L")),
                        Kit.Row("Barra laterale", null, Keys(Mod + "\\")),
                        Kit.Row("Cerca nei fogli", null, Keys(Mod + Shift + "F")),
                        Kit.Row("Cerca nel foglio", null, Keys(Mod + "F")),
                        Kit.Row("Zoom della nota", null, Keys(Mod + "+  " + Mod + "-  " + Mod + "0")))))),
            Panel(Kit.Columns(
                Kit.Stack(
                    Kit.Header("Salvataggio", first: true),
                    Kit.Group(
                        Kit.Row("Salvataggio automatico", "Senza, la nota si salva comunque alla chiusura e al blocco.", _autosave),
                        Kit.Row("Attesa prima di salvare (ms)", null, _autosaveMs))),
                Kit.Stack(
                    Kit.Header("Indirizzi nel testo", first: true),
                    Kit.Group(
                        Kit.Row("Una parola diventa host dopo (ms)",
                            "L'indirizzo sotto il cursore e' una parola che si sta scrivendo: diventa host quando il cursore se ne va o dopo questa attesa.", _hostMs))))),
            Panel(Kit.Columns(
                Kit.Stack(
                    Kit.Header("Monitoraggio", first: true),
                    Kit.Group(
                        Kit.Row("Controlla da solo gli host della nota", "Senza notifiche: il risultato si vede nei colori degli indirizzi.", _monitor),
                        Kit.Row("Ogni quanti secondi", null, _monitorSec)),
                    Kit.Header("Porte"),
                    Kit.Group(
                        Kit.Block("Porte da provare su ogni host", null, _ports, Spaced(_portsInfo, 6)),
                        Kit.Row("Analizza anche chi non risponde al ping", "Molti firewall scartano il ping ma rispondono sulle porte: piu' lento, piu' completo.", _deadHosts))),
                Kit.Stack(
                    Kit.Header("Ritmo", first: true),
                    Kit.Group(
                        Kit.Row("Host analizzati insieme", null, _threads),
                        Kit.Row("Connessioni aperte insieme", "Il sistema ha un numero finito di porte: alzare gli host senza alzare le connessioni non rende.", _sockets),
                        Kit.Row("Ping per host", null, _pingCount),
                        Kit.Row("Attesa del ping (ms)", null, _pingMs),
                        Kit.Row("Attesa di una porta (ms)", null, _portMs))))),
            Panel(Kit.Columns(
                Kit.Stack(
                    Kit.Header("Nomi", first: true),
                    Kit.Group(
                        Kit.Row("Nome host via DNS inverso", null, _dns),
                        Kit.Row("Server DNS", "Vuoto: quello di sistema.", _dnsServer),
                        Kit.Row("Nome, gruppo e MAC via NetBIOS", null, _netBios),
                        Kit.Row("Nome via mDNS (Bonjour)", null, _mdns)),
                    Kit.Header("Produttori dei MAC"),
                    Kit.Group(Kit.Row("Database IEEE", null, _oui).With(row => ((StackPanel)row.Children[0]).Children.Add(_ouiInfo)))),
                Kit.Stack(
                    Kit.Header("Servizi", first: true),
                    Services()))),
            _vault,
            Panel(Kit.Columns(
                Kit.Stack(
                    Kit.Header("Dati", first: true),
                    Kit.Group(Kit.Row("Cartella dei dati", null, openFolder).With(row => ((StackPanel)row.Children[0]).Children.Add(_dataFolder))),
                    Spaced(Kit.Note("La variabile DUCKNOTE_HOME sposta tutto altrove, per esempio su una chiavetta."), 8).With(note => note.Margin = new Thickness(16, 8, 16, 0)),
                    Kit.Header("Disinstallazione"),
                    Danger(Kit.Group(Kit.Row("Cancella tutti i dati",
                        "Nota, copie, impostazioni, scansioni e cassaforte: i byte vengono sovrascritti, poi i file rimossi. Non si torna indietro.", wipe)))),
                Kit.Stack(
                    Kit.Header("Verifica del codice", first: true),
                    Kit.Group(Kit.Block("SHA-256 dell'eseguibile in uso", "Da confrontare con le somme pubblicate con ogni release.", _fingerprint, _fingerprintPath)))))
        ];

        _tabs =
        [
            Segment("section", "Aspetto", true),
            Segment("section", "Nota"),
            Segment("section", "Scansione"),
            Segment("section", "Raccolta"),
            Segment("section", "Cassaforte"),
            Segment("section", "Sistema")
        ];

        foreach (RadioButton tab in _tabs)
        {
            tab.Height = 30;
            tab.Padding = new Thickness(14, 0);
            tab.FontSize = 12.5;
            tab.IsCheckedChanged += (_, _) =>
            {
                if (tab.IsChecked == true)
                {
                    OnSection(Array.IndexOf(_tabs, tab));
                }
            };
        }

        // ------------------------------------------------------------ pagina
        _overline.Bind(TextBlock.ForegroundProperty, _overline.GetResourceObservable("Accent"));
        TextBlock title = new() { Text = "Impostazioni" };
        title.Classes.Add("title");
        _sub.Classes.Add("muted");
        RenderOptions.SetBitmapInterpolationMode(_art, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);

        Grid head = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(_art);
        StackPanel words = new() { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _overline, title, _sub } };
        Grid.SetColumn(words, 1);
        head.Children.Add(words);

        Border tabs = Well(new Thickness(0, 12, 0, 2), _tabs);
        tabs.HorizontalAlignment = HorizontalAlignment.Left;

        Grid host = new() { ClipToBounds = true, Margin = new Thickness(0, 6, 0, 0) };
        host.Children.AddRange(_panels);

        Grid column = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*"), MaxWidth = 1100 };
        column.Children.Add(head);
        Grid.SetRow(tabs, 1);
        column.Children.Add(tabs);
        Grid.SetRow(host, 2);
        column.Children.Add(host);

        Content = new Border { Padding = new Thickness(20, 14, 20, 16), Child = column };

        Load();
        OnSection(0, animate: false);
        _loading = false;
    }

    private AppSettings S => _ws.Settings;

    /// <summary>Un'impostazione che conta per la finestra (il monitoraggio) e' cambiata.</summary>
    public event Action? Changed;

    /// <summary>Porta in primo piano la scheda Cassaforte.</summary>
    public void ShowVault()
    {
        _tabs[4].IsChecked = true;
        _vault.Offer();
    }

    public void OnShown()
    {
        _loading = true;
        Load();
        _loading = false;

        if (_section == 4)
        {
            _vault.OnShown();
        }
    }

    public void OnHidden()
    {
    }

    private void Load()
    {
        (S.FollowSystemTheme ? _themeSystem : S.Theme == "dark" ? _themeDark : _themeLight).IsChecked = true;

        _autosave.IsChecked = S.AutosaveEnabled;
        _autosaveMs.Text = Number(S.AutosaveDebounceMs);
        _hostMs.Text = Number(S.HostDebounceMs);

        _monitor.IsChecked = S.MonitorEnabled;
        _monitorSec.Text = Number(S.MonitorIntervalSec);
        _threads.Text = Number(S.MaxThreads);
        _sockets.Text = Number(S.MaxSockets);
        _pingCount.Text = Number(S.PingCount);
        _pingMs.Text = Number(S.PingTimeoutMs);
        _portMs.Text = Number(S.PortTimeoutMs);
        _ports.Text = S.Ports;
        _portsInfo.Text = $"{S.PortList().Count} porte. Separate da virgole o spazi.";
        _deadHosts.IsChecked = S.ScanDeadHosts;

        _dns.IsChecked = S.ResolveDns;
        _dnsServer.Text = S.DnsServer;
        _netBios.IsChecked = S.ProbeNetBios;
        _mdns.IsChecked = S.ProbeMdns;
        _ssdp.IsChecked = S.ProbeSsdp;
        _banners.IsChecked = S.ProbeBanners;
        _snmp.IsChecked = S.ProbeSnmp;
        _community.Text = S.SnmpCommunity;
        _shares.IsChecked = S.ProbeShares;
        _wmi.IsChecked = S.ProbeWmi;
        ShowOui();

        _dataFolder.Text = AppPaths.Folder;
        ShowFingerprint();
    }

    private void OnSection(int index, bool animate = true)
    {
        (string art, string title, string sub) = Sections[index];
        _art.Source = Art.Of($"art/{art}.png");
        _overline.Text = title.ToUpperInvariant();
        _sub.Text = index == 4 ? _vault.Summary : sub;

        for (int i = 0; i < _panels.Length; i++)
        {
            _panels[i].IsVisible = i == index;
        }

        if (index == 4)
        {
            _vault.OnShown();
        }

        if (animate && index != _section)
        {
            Control panel = _panels[index];
            TranslateTransform shift = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
            panel.RenderTransform = shift;
            double from = index > _section ? 34 : -34;
            Motion.Tween((panel, "carousel"), from, 0, 280, x => shift.X = x, null, Curves.CubicOut);
            Motion.Show(panel, rise: 0, ms: 220);
        }

        _section = index;
    }

    // ------------------------------------------------------------------ aspetto

    private void OnTheme()
    {
        if (_loading)
        {
            return;
        }

        S.FollowSystemTheme = _themeSystem.IsChecked == true;
        S.Theme = _themeDark.IsChecked == true ? "dark" : _themeLight.IsChecked == true ? "light" : Palette.SystemTheme();
        S.Save();
        Palette.Set(S.FollowSystemTheme ? "system" : S.Theme);
    }

    // ------------------------------------------------------------------ interruttori e numeri

    private void OnSwitch()
    {
        if (_loading)
        {
            return;
        }

        S.AutosaveEnabled = _autosave.IsChecked == true;
        S.MonitorEnabled = _monitor.IsChecked == true;
        S.ScanDeadHosts = _deadHosts.IsChecked == true;
        S.ResolveDns = _dns.IsChecked == true;
        S.ProbeNetBios = _netBios.IsChecked == true;
        S.ProbeMdns = _mdns.IsChecked == true;
        S.ProbeSsdp = _ssdp.IsChecked == true;
        S.ProbeBanners = _banners.IsChecked == true;
        S.ProbeSnmp = _snmp.IsChecked == true;
        S.ProbeShares = _shares.IsChecked == true;
        S.ProbeWmi = _wmi.IsChecked == true;
        S.Save();
        _ws.StartTimers();
        Changed?.Invoke();
    }

    private void OnNumbers()
    {
        if (_loading)
        {
            return;
        }

        S.AutosaveDebounceMs = Read(_autosaveMs, S.AutosaveDebounceMs, 200, 60000);
        S.HostDebounceMs = Read(_hostMs, S.HostDebounceMs, 500, 30000);
        S.MonitorIntervalSec = Read(_monitorSec, S.MonitorIntervalSec, 10, 86400);
        S.MaxThreads = Read(_threads, S.MaxThreads, 1, 1024);
        S.MaxSockets = Read(_sockets, S.MaxSockets, 16, 4096);
        S.PingCount = Read(_pingCount, S.PingCount, 1, 10);
        S.PingTimeoutMs = Read(_pingMs, S.PingTimeoutMs, 100, 10000);
        S.PortTimeoutMs = Read(_portMs, S.PortTimeoutMs, 50, 10000);
        S.Save();
        _ws.StartTimers();
        Changed?.Invoke();
    }

    private void OnPorts()
    {
        if (_loading)
        {
            return;
        }

        S.Ports = _ports.Text?.Trim() ?? string.Empty;
        S.Save();
        _portsInfo.Text = $"{S.PortList().Count} porte. Separate da virgole o spazi.";
    }

    private void OnTexts()
    {
        if (_loading)
        {
            return;
        }

        S.DnsServer = _dnsServer.Text?.Trim() ?? string.Empty;
        string community = _community.Text?.Trim() ?? string.Empty;
        S.SnmpCommunity = community.Length > 0 ? community : "public";
        S.Save();
    }

    /// <summary>Un numero fuori misura torna nel suo intervallo; uno illeggibile resta quello di prima.</summary>
    private static int Read(TextBox field, int current, int least, int most)
    {
        int value = int.TryParse(field.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int typed)
            ? Math.Clamp(typed, least, most)
            : current;

        if (field.Text != Number(value))
        {
            field.Text = Number(value);
            Kit.Shake(field);
        }

        return value;
    }

    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    /// <summary>Invio in una casella conferma il valore, come uscirne: le impostazioni valgono subito.</summary>
    private static void Confirmed(TextBox box, Action apply)
    {
        box.LostFocus += (_, _) => apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                apply();
            }
        };
    }

    // ------------------------------------------------------------------ produttori

    private void ShowOui()
    {
        _ouiInfo.Text = File.Exists(AppPaths.Oui)
            ? string.Create(CultureInfo.CurrentCulture, $"{_ws.Session.Vendors:N0} produttori, oui.txt del {File.GetLastWriteTime(AppPaths.Oui):d}.")
            : string.Create(CultureInfo.CurrentCulture, $"{_ws.Session.Vendors:N0} produttori dalla tabella integrata.");
    }

    private async Task OnOui()
    {
        object? before = _oui.Content;
        _oui.IsEnabled = false;
        _oui.Content = "Scaricamento...";

        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DuckNote");
            byte[] registry = await http.GetByteArrayAsync(IeeeRegistry);

            if (registry.Length < 100_000)
            {
                throw new InvalidDataException("il file arrivato e' troppo piccolo per essere il registro IEEE");
            }

            AppPaths.Ensure();
            await File.WriteAllBytesAsync(AppPaths.Oui, registry);
            _ws.Session.ReloadVendors();
            ShowOui();
            Notices.Current?.Success("Produttori aggiornati", _ouiInfo.Text ?? string.Empty);
        }
        catch (Exception problem) when (problem is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notices.Current?.Error("Aggiornamento non riuscito", problem.Message);
        }
        finally
        {
            _oui.Content = before;
            _oui.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------ sistema

    private void ShowFingerprint()
    {
        string? path = Environment.ProcessPath;
        _fingerprintPath.Text = path ?? string.Empty;

        try
        {
            _fingerprint.Text = path is null ? "non disponibile" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            _fingerprint.Text = "non leggibile: " + problem.Message;
        }
    }

    private async Task OnOpenFolder()
    {
        try
        {
            AppPaths.Ensure();

            if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher || !await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppPaths.Folder)))
            {
                _ws.Say("La cartella non si apre da qui: " + AppPaths.Folder);
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _ws.Say("La cartella non si apre: " + problem.Message);
        }
    }

    private async Task OnWipe()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner
            || !await Message.Ask(owner, "Cancellare tutti i dati?",
                $"Ogni file in {AppPaths.Folder} viene sovrascritto e rimosso: nota, copie, impostazioni, scansioni e cassaforte. Non si torna indietro.",
                "Cancella tutto", danger: true))
        {
            return;
        }

        _ws.Wipe();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // ------------------------------------------------------------------ pezzi

    private Border Services()
    {
        List<Control> rows =
        [
            Kit.Row("Descrizione via SSDP/UPnP", null, _ssdp),
            Kit.Row("Banner, titolo web e certificato TLS", null, _banners),
            Kit.Row("Interrogazione SNMP v2c", null, _snmp),
            Kit.Row("Community SNMP", null, _community),
            Kit.Row("Condivisioni SMB", OperatingSystem.IsMacOS() ? "Piu' lento. Usa smbutil del sistema." : "Piu' lento.", _shares)
        ];

        // l'inventario WMI c'e' solo su Windows: altrove la riga non avrebbe effetto
        if (OperatingSystem.IsWindows())
        {
            rows.Add(Kit.Row("Inventario via WMI/WinRM", "Credenziali dell'utente corrente; fuori dominio serve un account locale.", _wmi));
        }

        return Kit.Group([.. rows]);
    }

    private static ScrollViewer Panel(Control content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsVisible = false };

    private static RadioButton Segment(string group, string text, bool chosen = false) =>
        new() { Theme = Themes.Of("Segment"), GroupName = group, Content = text, IsChecked = chosen };

    private static Border Well(Thickness margin, params RadioButton[] segments) => Segments.Well(margin, segments);

    private static string Mod => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";

    private static string Shift => OperatingSystem.IsMacOS() ? "⇧" : "Maiusc+";

    private static Border Keys(string text)
    {
        TextBlock keys = new() { Text = text, FontFamily = Mono, FontSize = 11.5 };
        keys.Bind(TextBlock.ForegroundProperty, keys.GetResourceObservable("LabelSecondary"));
        Border chip = new() { Child = keys, Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(6) };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("BgPressed"));
        return chip;
    }

    private static T Spaced<T>(T control, double top) where T : Control
    {
        control.Margin = new Thickness(control.Margin.Left, top, control.Margin.Right, control.Margin.Bottom);
        return control;
    }

    private static Border Danger(Border group)
    {
        group.Bind(Border.BorderBrushProperty, group.GetResourceObservable("RedBorder"));
        return group;
    }

    private static FontFamily Mono =>
        Application.Current?.TryFindResource("Mono", out object? family) == true && family is FontFamily mono ? mono : FontFamily.Default;
}

internal static class Tweaks
{
    /// <summary>Un ritocco al volo su un controllo appena creato, senza spezzare l'espressione.</summary>
    public static T With<T>(this T control, Action<T> change)
    {
        change(control);
        return control;
    }
}
