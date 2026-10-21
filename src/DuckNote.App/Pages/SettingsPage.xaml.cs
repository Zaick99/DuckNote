using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DuckNote.App.Dialogs;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.Notes.Settings;

namespace DuckNote.App.Pages;

/// <summary>Le impostazioni, a sezioni. Ogni modifica si applica e si salva subito.</summary>
public partial class SettingsPage : UserControl, IDuckPage
{
    private const string IeeeRegistry = "https://standards-oui.ieee.org/oui/oui.txt";

    private static readonly (string Art, string Title, string Sub)[] Sections =
    [
        ("settings", "Aspetto", "Tema chiaro, scuro o come Windows, e le papere sullo sfondo."),
        ("note", "Nota", "Quando si salva, e quando una parola scritta diventa un host da controllare."),
        ("network", "Scansione", "Il monitoraggio degli host della nota, il ritmo delle analisi e le porte da provare."),
        ("hosts", "Raccolta", "Cosa chiedere a ogni host: nomi, produttori, servizi, inventario."),
        ("vault", "Cassaforte", ""),
        ("export", "Sistema", "Dove stanno i dati, la verifica dell'eseguibile, la disinstallazione.")
    ];

    private readonly Workspace _ws;
    private int _section;
    private bool _loading = true;

    public SettingsPage(Workspace ws)
    {
        _ws = ws;
        InitializeComponent();
        Vault = new VaultPage(ws) { Embedded = true };
        Vault.SummaryChanged += () =>
        {
            if (SecVault.IsChecked == true) SectionSub.Text = Vault.Summary;
        };
        PanelVault.Content = Vault;
        Load();
        OnSection(this, new RoutedEventArgs());
        _loading = false;
    }

    private AppSettings S => _ws.Settings;

    /// <summary>La cassaforte, che qui e' una scheda.</summary>
    public VaultPage Vault { get; }

    /// <summary>Porta in primo piano la scheda Cassaforte.</summary>
    public void ShowVault()
    {
        SecVault.IsChecked = true;
        Vault.OnShown();
    }

    public void OnShown()
    {
        _loading = true;
        Load();
        _loading = false;

        if (SecVault.IsChecked == true)
        {
            Vault.OnShown();
        }
    }

    private void Load()
    {
        (S.FollowSystemTheme ? ThemeSystem : S.Theme == "dark" ? ThemeDark : ThemeLight).IsChecked = true;

        Ducks.IsChecked = S.DuckBackground;
        DuckCount.Text = Number(S.DuckCount);
        DuckOpacity.Text = Number(S.DuckOpacity);

        Autosave.IsChecked = S.AutosaveEnabled;
        AutosaveMs.Text = Number(S.AutosaveDebounceMs);
        HostMs.Text = Number(S.HostDebounceMs);

        Monitor.IsChecked = S.MonitorEnabled;
        MonitorSec.Text = Number(S.MonitorIntervalSec);
        Threads.Text = Number(S.MaxThreads);
        Sockets.Text = Number(S.MaxSockets);
        PingCount.Text = Number(S.PingCount);
        PingMs.Text = Number(S.PingTimeoutMs);
        PortMs.Text = Number(S.PortTimeoutMs);
        Ports.Text = S.Ports;
        PortsInfo.Text = $"{S.PortList().Count} porte. Separate da virgole o spazi.";
        DeadHosts.IsChecked = S.ScanDeadHosts;

        Dns.IsChecked = S.ResolveDns;
        DnsServer.Text = S.DnsServer;
        NetBios.IsChecked = S.ProbeNetBios;
        Mdns.IsChecked = S.ProbeMdns;
        Ssdp.IsChecked = S.ProbeSsdp;
        Banners.IsChecked = S.ProbeBanners;
        Snmp.IsChecked = S.ProbeSnmp;
        Community.Text = S.SnmpCommunity;
        Shares.IsChecked = S.ProbeShares;
        Wmi.IsChecked = S.ProbeWmi;
        ShowOui();

        DataFolder.Text = AppPaths.Folder;
        ShowFingerprint();
    }

    private void OnSection(object sender, RoutedEventArgs e)
    {
        // parte anche durante InitializeComponent (radio preselezionato): i pannelli possono essere null
        if (PanelLook is null || PanelNote is null || PanelScan is null || PanelProbe is null || PanelVault is null || PanelSystem is null || Vault is null)
        {
            return;
        }

        FrameworkElement[] panels = [PanelLook, PanelNote, PanelScan, PanelProbe, PanelVault, PanelSystem];
        RadioButton[] tabs = [SecLook, SecNote, SecScan, SecProbe, SecVault, SecSystem];
        int index = Math.Max(0, Array.FindIndex(tabs, tab => tab.IsChecked == true));
        (string art, string title, string sub) = Sections[index];

        SectionArt.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/art/{art}.png"));
        SectionTitle.Text = title.ToUpperInvariant();
        SectionSub.Text = panels[index] == PanelVault ? Vault.Summary : sub;

        if (panels[index] == PanelVault)
        {
            Vault.OnShown();
        }

        if (!IsLoaded || index == _section)
        {
            for (int i = 0; i < panels.Length; i++)
            {
                panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }

            _section = index;
            return;
        }

        Fx.Pop(SectionArt);
        Fx.Carousel(panels[_section], panels[index], index > _section);
        _section = index;
    }

    // ------------------------------------------------------------------ aspetto

    private void OnTheme(object sender, RoutedEventArgs e)
    {
        if (_loading || ThemeLight is null || ThemeDark is null)
        {
            return;
        }

        S.FollowSystemTheme = ThemeSystem.IsChecked == true;
        S.Theme = ThemeDark.IsChecked == true ? "dark" : ThemeLight.IsChecked == true ? "light" : ThemeManager.SystemTheme();
        S.Save();
        ThemeManager.Set(S.ThemePreference);
    }

    private void OnDucks(object sender, RoutedEventArgs e)
    {
        S.DuckBackground = Ducks.IsChecked == true;
        S.Save();
        Shell()?.RebuildDucks();
    }

    private void OnDuckNumbers(object sender, RoutedEventArgs e)
    {
        S.DuckCount = Read(DuckCount, S.DuckCount, 0, 200);
        S.DuckOpacity = Read(DuckOpacity, S.DuckOpacity, 0, 100);
        S.Save();
        Shell()?.RebuildDucks();
    }

    // ------------------------------------------------------------------ interruttori e numeri

    private void OnSwitch(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        S.AutosaveEnabled = Autosave.IsChecked == true;
        S.MonitorEnabled = Monitor.IsChecked == true;
        S.ScanDeadHosts = DeadHosts.IsChecked == true;
        S.ResolveDns = Dns.IsChecked == true;
        S.ProbeNetBios = NetBios.IsChecked == true;
        S.ProbeMdns = Mdns.IsChecked == true;
        S.ProbeSsdp = Ssdp.IsChecked == true;
        S.ProbeBanners = Banners.IsChecked == true;
        S.ProbeSnmp = Snmp.IsChecked == true;
        S.ProbeShares = Shares.IsChecked == true;
        S.ProbeWmi = Wmi.IsChecked == true;
        S.Save();
        _ws.StartTimers();
        Shell()?.ShowMonitor();
    }

    private void OnNumbers(object sender, RoutedEventArgs e)
    {
        S.AutosaveDebounceMs = Read(AutosaveMs, S.AutosaveDebounceMs, 200, 60000);
        S.HostDebounceMs = Read(HostMs, S.HostDebounceMs, 500, 30000);
        S.MonitorIntervalSec = Read(MonitorSec, S.MonitorIntervalSec, 10, 86400);
        S.MaxThreads = Read(Threads, S.MaxThreads, 1, 1024);
        S.MaxSockets = Read(Sockets, S.MaxSockets, 16, 4096);
        S.PingCount = Read(PingCount, S.PingCount, 1, 10);
        S.PingTimeoutMs = Read(PingMs, S.PingTimeoutMs, 100, 10000);
        S.PortTimeoutMs = Read(PortMs, S.PortTimeoutMs, 50, 10000);
        S.Save();
        _ws.StartTimers();
        Shell()?.ShowMonitor();
    }

    private void OnPorts(object sender, RoutedEventArgs e)
    {
        S.Ports = Ports.Text.Trim();
        S.Save();
        PortsInfo.Text = $"{S.PortList().Count} porte. Separate da virgole o spazi.";
    }

    private void OnTexts(object sender, RoutedEventArgs e)
    {
        S.DnsServer = DnsServer.Text.Trim();
        S.SnmpCommunity = Community.Text.Trim().Length > 0 ? Community.Text.Trim() : "public";
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
            Fx.Shake(field);
        }

        return value;
    }

    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    // ------------------------------------------------------------------ produttori

    private void ShowOui()
    {
        OuiInfo.Text = File.Exists(AppPaths.Oui)
            ? string.Create(CultureInfo.CurrentCulture, $"{_ws.Session.Vendors:N0} produttori, oui.txt del {File.GetLastWriteTime(AppPaths.Oui):d}.")
            : string.Create(CultureInfo.CurrentCulture, $"{_ws.Session.Vendors:N0} produttori dalla tabella integrata.");
    }

    private async void OnOui(object sender, RoutedEventArgs e)
    {
        await Busy.Run(BtnOui, "Scaricamento...", async () =>
        {
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
                Toast.Success("Produttori aggiornati", OuiInfo.Text);
            }
            catch (Exception problem) when (problem is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                Toast.Error("Aggiornamento non riuscito", problem.Message);
            }
        });
    }

    // ------------------------------------------------------------------ sistema

    private void ShowFingerprint()
    {
        string? path = Environment.ProcessPath;
        FingerprintPath.Text = path ?? string.Empty;

        try
        {
            Fingerprint.Text = path is null ? "non disponibile" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            Fingerprint.Text = "non leggibile: " + problem.Message;
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.Ensure();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception problem) when (problem is System.ComponentModel.Win32Exception or IOException)
        {
            _ws.Say("La cartella non si apre: " + problem.Message);
        }
    }

    private void OnWipe(object sender, RoutedEventArgs e)
    {
        if (!DuckDialog.Confirm(Window.GetWindow(this), "Cancellare tutti i dati?",
                $"Ogni file in {AppPaths.Folder} viene sovrascritto e rimosso: nota, copie, impostazioni, scansioni e cassaforte. Non si torna indietro.",
                "Cancella tutto", danger: true))
        {
            return;
        }

        _ws.Wipe();
        Application.Current.Shutdown();
    }

    private MainWindow? Shell() => Window.GetWindow(this) as MainWindow;
}
