using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DuckNote.App.Storage;
using DuckNote.App.Theme;
using DuckNote.App.Vault;

namespace DuckNote.App.Views;

public enum SecurityRequest
{
    None,
    Enable,
    ChangePassword,
    Disable,
    Wipe
}

public partial class PreferencesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly bool _vaultExists;
    private readonly bool _vaultOpen;

    public SecurityRequest Request { get; private set; } = SecurityRequest.None;

    public PreferencesWindow(
        AppSettings settings, string theme, bool vaultExists, bool vaultOpen, ResourceDictionary ownerResources)
    {
        InitializeComponent();

        _settings = settings;
        _vaultExists = vaultExists;
        _vaultOpen = vaultOpen;

        Resources.MergedDictionaries.Add(ownerResources);
        ThemeTokens.Apply(Resources, theme);

        ContentRendered += (_, _) => Motion.OpenDialog(PRoot);

        PTitleBar.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };

        PClose.Click += (_, _) => Close();
        PCancel.Click += (_, _) => Close();
        POk.Click += (_, _) => Accept();

        PSecOn.Click += (_, _) => Ask(SecurityRequest.Enable);
        PSecPwd.Click += (_, _) => Ask(SecurityRequest.ChangePassword);
        PSecOff.Click += (_, _) => Ask(SecurityRequest.Disable);
        PWipe.Click += (_, _) => Ask(SecurityRequest.Wipe);

        Load();
    }

    private void Load()
    {
        PSysTheme.IsChecked = _settings.FollowSystemTheme;
        PTheme.SelectedIndex = _settings.Theme == "dark" ? 1 : 0;
        PLiveFmt.IsChecked = _settings.LiveFormatting;

        PDucks.IsChecked = _settings.DuckBackground;
        PDuckN.Text = Number(_settings.DuckCount);
        PDuckOp.Text = Number(_settings.DuckOpacity);

        PAutosave.IsChecked = _settings.AutosaveEnabled;
        PAutoMs.Text = Number(_settings.AutosaveDebounceMs);
        PMonitor.IsChecked = _settings.MonitorEnabled;
        PMonSec.Text = Number(_settings.MonitorIntervalSec);

        PThreads.Text = Number(_settings.MaxThreads);
        PPingN.Text = Number(_settings.PingCount);
        PPingMs.Text = Number(_settings.PingTimeoutMs);
        PPortMs.Text = Number(_settings.PortTimeoutMs);
        PPorts.Text = _settings.Ports;
        PDead.IsChecked = _settings.ScanDeadHosts;

        PDns.IsChecked = _settings.ResolveDns;
        PDnsServer.Text = _settings.DnsServer;
        PNetBios.IsChecked = _settings.ProbeNetBios;
        PMdns.IsChecked = _settings.ProbeMdns;
        PSsdp.IsChecked = _settings.ProbeSsdp;
        PBanner.IsChecked = _settings.ProbeBanners;
        PSnmp.IsChecked = _settings.ProbeSnmp;
        PCommunity.Text = _settings.SnmpCommunity;
        PShares.IsChecked = _settings.ProbeShares;
        PWmi.IsChecked = _settings.ProbeWmi;

        PLockMin.Text = Number(_settings.AutoLockMinutes);

        PParallel.IsChecked = true;
        PParallel.IsEnabled = false;
        PParallelInfo.Text = "Un motore solo, sempre asincrono.";

        POui.Click += (_, _) => POuiInfo.Text = "Scaricamento non ancora portato.";

        ShowSecurity();
    }

    private void ShowSecurity()
    {
        string token = _vaultOpen ? "Green" : _vaultExists ? "Orange" : "LabelQuaternary";
        PSecDot.Fill = ThemeTokens.Brush(ThemeTokens.Colour(_settings.Theme, token));

        PSecState.Text = _vaultOpen ? "Attiva, cassaforte aperta"
            : _vaultExists ? "Attiva, cassaforte chiusa"
            : "Non attiva";

        PSecInfo.Text = _vaultExists
            ? "Nota, nota precedente, ultima scansione ed esclusioni vivono dentro store.bin. " +
              "Niente viene scritto in chiaro finche' il contenitore esiste."
            : "La nota e' su disco in chiaro. Attivando la cifratura finisce dentro un contenitore " +
              "unico, protetto da Argon2id su 256 MiB e AES-256.";

        PLockInfo.Text = "Minuti di inattivita' prima del blocco automatico. 0 lo disattiva.";

        PSecOn.IsEnabled = !_vaultExists;
        PSecPwd.IsEnabled = _vaultOpen;
        PSecOff.IsEnabled = _vaultOpen;
    }

    private void Accept()
    {
        _settings.FollowSystemTheme = PSysTheme.IsChecked == true;
        _settings.Theme = PTheme.SelectedIndex == 1 ? "dark" : "light";
        _settings.LiveFormatting = PLiveFmt.IsChecked == true;

        _settings.DuckBackground = PDucks.IsChecked == true;
        _settings.DuckCount = Clamp(PDuckN.Text, _settings.DuckCount, 0, 200);
        _settings.DuckOpacity = Clamp(PDuckOp.Text, _settings.DuckOpacity, 0, 100);

        _settings.AutosaveEnabled = PAutosave.IsChecked == true;
        _settings.AutosaveDebounceMs = Clamp(PAutoMs.Text, _settings.AutosaveDebounceMs, 200, 60000);
        _settings.MonitorEnabled = PMonitor.IsChecked == true;
        _settings.MonitorIntervalSec = Clamp(PMonSec.Text, _settings.MonitorIntervalSec, 10, 86400);

        _settings.MaxThreads = Clamp(PThreads.Text, _settings.MaxThreads, 1, 512);
        _settings.PingCount = Clamp(PPingN.Text, _settings.PingCount, 1, 10);
        _settings.PingTimeoutMs = Clamp(PPingMs.Text, _settings.PingTimeoutMs, 100, 10000);
        _settings.PortTimeoutMs = Clamp(PPortMs.Text, _settings.PortTimeoutMs, 50, 10000);
        _settings.Ports = PPorts.Text.Trim();
        _settings.ScanDeadHosts = PDead.IsChecked == true;

        _settings.ResolveDns = PDns.IsChecked == true;
        _settings.DnsServer = PDnsServer.Text.Trim();
        _settings.ProbeNetBios = PNetBios.IsChecked == true;
        _settings.ProbeMdns = PMdns.IsChecked == true;
        _settings.ProbeSsdp = PSsdp.IsChecked == true;
        _settings.ProbeBanners = PBanner.IsChecked == true;
        _settings.ProbeSnmp = PSnmp.IsChecked == true;
        _settings.SnmpCommunity = PCommunity.Text.Trim();
        _settings.ProbeShares = PShares.IsChecked == true;
        _settings.ProbeWmi = PWmi.IsChecked == true;

        _settings.AutoLockMinutes = Clamp(PLockMin.Text, _settings.AutoLockMinutes, 0, 1440);

        _settings.Save();
        DialogResult = true;
    }

    private void Ask(SecurityRequest request)
    {
        if (request == SecurityRequest.Wipe
            && MessageBox.Show(this,
                "Rimuove ogni file di DuckNote da %APPDATA%, coprendone i byte. Non si torna indietro.",
                "Disinstallazione", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        Request = request;
        Accept();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Clamp(string text, int fallback, int low, int high) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, low, high)
            : fallback;
}
