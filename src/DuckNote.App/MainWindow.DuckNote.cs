using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuckNote.App.Storage;
using System.Windows.Threading;
using DuckNote.App.Pages;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.App.Vault;

namespace DuckNote.App;

/// <summary>
/// Quel che la shell fa per DuckNote: la schermata di blocco, il riquadro della cassaforte al
/// posto dell'account, la scheda del monitoraggio in fondo alla sidebar, i contatori sulle voci,
/// le scorciatoie (Ctrl+L, Ctrl+F) e le papere che si fermano mentre si scrive.
/// </summary>
public partial class MainWindow
{
    private void WireWorkspace()
    {
        Lock.Attach(_ws);
        Lock.Opened += () =>
        {
            ShowVault();
            Navigate("notes");
            SetStatus($"Riaperta alle {DateTime.Now:HH:mm}.");
        };

        _ws.Status += message => Dispatcher.BeginInvoke(() => SetStatus(message));
        _ws.Locked += () => Dispatcher.BeginInvoke(ShowLock);
        _ws.VaultChanged += () => Dispatcher.BeginInvoke(ShowVault);
        _ws.HostsChanged += () => Dispatcher.BeginInvoke(ShowHosts);
        _ws.Session.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            ShowMonitor();
            ShowHosts();
        });
        _ws.Session.Finished += message =>
        {
            // le analisi partite da sole (monitoraggio, indirizzi appena scritti) non disturbano:
            // il risultato si vede nei colori degli indirizzi e nella scheda in fondo alla sidebar
            if (!_ws.ScanIsAutomatic)
            {
                Dispatcher.BeginInvoke(() => Announce(message));
            }
        };

        _ws.HostRequested += host => Dispatcher.BeginInvoke(() =>
        {
            Navigate("network");

            if (Page("network") is NetworkPage network)
            {
                network.Reveal(host);
            }
        });

        // la pagina della nota puo' non esistere ancora: prima la si apre, poi le si consegna la tabella
        _ws.TableToNote += (header, rows) => Dispatcher.BeginInvoke(() =>
        {
            Navigate("notes");

            if (Page("notes") is NotesPage notes)
            {
                notes.InsertTable(header, rows);
            }
        });

        AccountClicked += () =>
        {
            if (_ws.Vault.IsOpen && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                _ws.Lock("a mano");
                return;
            }

            ShowAccountMenu(BtnAccount, withExit: true);
        };

        _duckRest.Tick += (_, _) =>
        {
            _duckRest.Stop();
            DucksAnimate = _ws.Settings.DuckBackground;
        };

        PreviewKeyDown += OnShortcut;
        PreviewMouseMove += (_, _) => _ws.Activity();
        PreviewMouseDown += (_, _) => _ws.Activity();
    }

    private void Start()
    {
        _ws.Start();

        // la nota c'e' sempre: i suoi fogli e i suoi comandi stanno nella sidebar
        var notes = (NotesPage)Open("notes");
        NavigatorHost.Content = notes.Navigator;
        CommandsHost.Content = notes.Commands;
        notes.Wanted += () =>
        {
            if (_current != "notes") Navigate("notes");
        };

        ShowUser();
        ShowVault();
        Version? version = typeof(MainWindow).Assembly.GetName().Version;
        StatusRight.Text = version is null ? string.Empty : $"v{version.ToString(3)}";
        StatusRight.ToolTip = $"DuckNote {StatusRight.Text}";
        ShowHosts();
        ShowMonitor();

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
            OpenVault().Offer();
        }
    }

    // ---------------------------------------------------------------- menu dell'utente e lucchetto

    /// <summary>
    /// Il menu sotto l'utente (o sotto il lucchetto): con la cifratura attiva propone di bloccare
    /// la nota, senza propone di impostarla; dall'utente si puo' anche uscire.
    /// </summary>
    private void ShowAccountMenu(UIElement target, bool withExit)
    {
        if (_ws.IsLocked)
        {
            return;
        }

        bool vault = _ws.Vault.IsOpen;
        MenuTitle.Text = vault ? "La nota e' cifrata" : "La nota non e' cifrata";
        MenuText.Text = vault
            ? "Bloccandola si salva, la chiave lascia la memoria e per riaprirla serve la password."
            : "Vuoi impostare la cifratura? Ti porto in Impostazioni, scheda Cassaforte.";
        MenuLock.Visibility = vault ? Visibility.Visible : Visibility.Collapsed;
        MenuVault.Visibility = vault ? Visibility.Collapsed : Visibility.Visible;
        MenuSep.Visibility = MenuExit.Visibility = withExit ? Visibility.Visible : Visibility.Collapsed;
        AccountMenu.PlacementTarget = target;
        AccountMenu.IsOpen = true;
    }

    private void OnLockButton(object sender, RoutedEventArgs e)
    {
        if (_ws.IsLocked)
        {
            return;
        }

        if (_ws.Vault.IsOpen)
        {
            _ws.Lock("a mano");
            return;
        }

        ShowAccountMenu(BtnLock, withExit: false);
    }

    private void OnMenuLock(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = false;
        _ws.Lock("a mano");
    }

    private void OnMenuVault(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = false;
        OpenVault();
    }

    private void OnMenuExit(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = false;
        OnClose(sender, e);
    }

    /// <summary>La cassaforte e' una scheda delle Impostazioni.</summary>
    private VaultPage OpenVault()
    {
        Navigate("settings");
        var settings = (SettingsPage)Open("settings");
        settings.ShowVault();
        return settings.Vault;
    }

    /// <summary>La pagina gia' creata per quella chiave (null se non e' ancora stata aperta).</summary>
    private FrameworkElement? Page(string key) => _cache.GetValueOrDefault(key);

    private void ShowLock()
    {
        Lock.Present();
        ShowVault();
    }

    /// <summary>
    /// Il riquadro in cima alla sidebar: chi sta usando DuckNote, con la sua papera (una per
    /// lettera iniziale del nome) e un lucchetto sull'avatar che dice com'e' la cassaforte.
    /// </summary>
    private void ShowUser()
    {
        string name = CurrentUser.DisplayName;
        BtnAccount.Visibility = Visibility.Visible;
        AccountName.Text = name;
        AccountSub.Text = CurrentUser.Where;
        AccountPhoto.Fill = new ImageBrush(new BitmapImage(CurrentUser.DuckFor(name))) { Stretch = Stretch.UniformToFill };
    }

    private void ShowVault()
    {
        (string token, string glyph, string text, string tip) = _ws.Vault.IsOpen
            ? ("Green", "\uE72E", "Nota cifrata, aperta", "Clic: blocca la nota o esci. Maiusc+clic (o Ctrl+L) la blocca subito.")
            : _ws.IsLocked
                ? ("Orange", "\uE72E", "Nota chiusa a chiave", "Serve la password per aprirla")
                : ("LabelQuaternary", "\uE785", "Nota in chiaro", "La nota non e' cifrata. Clic: imposta la cifratura o esci.");

        AccountBadge.SetResourceReference(Border.BackgroundProperty, token);
        AccountBadgeGlyph.Text = glyph;
        AccountVault.Text = text;
        AccountVault.SetResourceReference(TextBlock.ForegroundProperty, token == "LabelQuaternary" ? "LabelTertiary" : token);
        BtnAccount.ToolTip = tip;

        // il lucchetto in alto a destra: chiuso e arancio se la cifratura c'e', aperto e grigio se no
        LockGlyph.Text = _ws.Vault.IsOpen || _ws.IsLocked ? "\uE72E" : "\uE785";
        LockGlyph.SetResourceReference(TextBlock.ForegroundProperty, _ws.Vault.IsOpen ? "Orange" : "LabelSecondary");
        BtnLock.ToolTip = _ws.Vault.IsOpen ? "Blocca la nota (Ctrl+L)" : "Cifratura non attiva: clic per impostarla";

        // a nota bloccata restano solo versione e giorno/notte
        BtnLock.Visibility = _ws.IsLocked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowHosts()
    {
        // accanto a Rete: in rosso gli host spenti, in verde quelli che rispondono
        int down = _ws.Session.DownCount, up = _ws.Session.UpCount;
        SetBadge("network", down, "Red", down == 1 ? "1 host spento" : $"{down} host spenti");
        SetBadge("network", up, "Green", up == 1 ? "1 host attivo" : $"{up} host attivi");
    }

    /// <summary>La scheda in fondo alla sidebar: monitoraggio a riposo, oppure la scansione in corso.</summary>
    public void ShowMonitor()
    {
        var session = _ws.Session;

        if (session.IsRunning)
        {
            SetStatusCard("Analisi in corso", session.Message, session.Progress, "Blue",
                string.Create(CultureInfo.CurrentCulture, $"{session.Progress:P0}"), pulse: true);
            return;
        }

        if (_ws.IsLocked)
        {
            SetStatusCard("Monitoraggio", "Fermo finche' la nota e' chiusa a chiave.", null, "Gray");
            return;
        }

        int hosts = _ws.NoteHosts(settled: true).Count;

        SetStatusCard(
            "Monitoraggio",
            _ws.Settings.MonitorEnabled
                ? string.Create(CultureInfo.CurrentCulture, $"{hosts} host della nota, ogni {_ws.Settings.MonitorIntervalSec} s. {session.UpCount} attivi, {session.DownCount} spenti.")
                : "Spento: gli host si controllano solo a mano.",
            null,
            _ws.Settings.MonitorEnabled ? (session.DownCount > 0 ? "Orange" : "Green") : "Gray",
            _ws.Settings.MonitorEnabled ? string.Create(CultureInfo.CurrentCulture, $"{_ws.Settings.MonitorIntervalSec}s") : "fermo");
    }

    private static void Announce(string message)
    {
        if (message.StartsWith("Analisi interrotta", StringComparison.Ordinal))
        {
            Toast.Error("Analisi non riuscita", message);
        }
        else if (message.StartsWith("Fermata", StringComparison.Ordinal))
        {
            Toast.Info("Analisi fermata", message);
        }
        else if (!message.StartsWith("Indica", StringComparison.Ordinal) && !message.StartsWith("Niente", StringComparison.Ordinal))
        {
            Toast.Success("Analisi finita", message);
        }
    }

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        _ws.Activity();

        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control && _ws.Vault.IsOpen)
        {
            _ws.Lock("a mano");
            e.Handled = true;
            return;
        }

        if (_ws.IsLocked)
        {
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && Page("notes") is NotesPage notes)
        {
            if (_current != "notes") Navigate("notes");
            e.Handled = notes.HandleShortcut(e);
            return;
        }

        if (Page("notes") is NotesPage open && PageHost.Content == open)
        {
            e.Handled = open.HandleShortcut(e);
        }
    }

    /// <summary>Le papere si fermano mentre si scrive, e ripartono poco dopo.</summary>
    public void RestDucks()
    {
        if (!_ws.Settings.DuckBackground)
        {
            return;
        }

        DucksAnimate = false;
        _duckRest.Stop();
        _duckRest.Start();
    }

    public void RebuildDucks()
    {
        _flock.Configure(_ws.Settings.DuckBackground, _ws.Settings.DuckCount, _ws.Settings.DuckOpacity / 100.0);
        DucksAnimate = _ws.Settings.DuckBackground;
    }
}
