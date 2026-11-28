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
using DuckNote.Notes.Calendar;
using CalendarPage = DuckNote.App.Pages.CalendarPage;

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
        _ws.VaultChanged += () => Dispatcher.BeginInvoke(() =>
        {
            ShowVault();
            ShowSaved();
        });
        // gia' raggruppato dal Workspace: qualche volta al secondo anche durante un'analisi
        _ws.HostsChanged += () =>
        {
            ShowMonitor();
            ShowHosts();
            ShowMapCount();
        };
        _ws.Unsaved += () => Dispatcher.BeginInvoke(ShowMapCount);
        // un interruttore dei gruppi in Rete cambia quello che il monitoraggio controlla
        _ws.MonitorChanged += ShowMonitor;
        _ws.NoteReplaced += () => Dispatcher.BeginInvoke(() =>
        {
            ShowMapCount();
            ShowCalendarCount();
        });
        _ws.CalendarChanged += () => Dispatcher.BeginInvoke(ShowCalendarCount);
        _ws.CalendarTick += ShowCalendarCount;
        _ws.RemindersDue += alerts => Dispatcher.BeginInvoke(() => Remind(alerts));
        _ws.CalendarOpened += summary => Dispatcher.BeginInvoke(() => Summarize(summary));
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
                LockNow();
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
        AccountMenu.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                AccountMenu.IsOpen = false;
                e.Handled = true;
            }
        };
        PreviewMouseMove += (_, _) => _ws.Activity();
        PreviewMouseDown += (_, e) =>
        {
            _ws.Activity();
            DismissPeek(e);
        };
    }

    private void Start()
    {
        _ws.Start();

        // la nota c'e' sempre: i suoi fogli e i suoi comandi stanno nella sidebar
        var notes = (NotesPage)Open("notes");
        NavigatorHost.Content = notes.Navigator;
        CommandsHost.Content = notes.Commands;
        ToolbarHost.Content = notes.Toolbar;
        notes.Wanted += () =>
        {
            if (_current != "notes") Navigate("notes");
        };
        notes.SidebarWanted += WantSidebar;

        // la sidebar resta come la si e' lasciata: nascosta, tutta la finestra e' del foglio
        ShowSidebarButton();
        SetSide(_ws.Settings.SidebarHidden ? Side.Hidden : Side.Docked, animate: false);
        PlaceToolbar(animate: false);

        ShowUser();
        ShowVault();
        Version? version = typeof(MainWindow).Assembly.GetName().Version;
        StatusRight.Text = version is null ? string.Empty : $"v{version.ToString(3)}";
        StatusRight.ToolTip = $"DuckNote {StatusRight.Text}";
        ShowHosts();
        ShowMonitor();
        ShowSaved();

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

        // da tastiera: il fuoco va sulla prima voce, Esc chiude e torna dov'era
        Button first = vault ? MenuLock : MenuVault;
        Dispatcher.BeginInvoke(() => first.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnLockButton(object sender, RoutedEventArgs e)
    {
        if (_ws.IsLocked)
        {
            return;
        }

        if (_ws.Vault.IsOpen)
        {
            LockNow();
            return;
        }

        ShowAccountMenu(BtnLock, withExit: false);
    }

    private void OnMenuLock(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = false;
        LockNow();
    }

    /// <summary>Blocca a mano: prima quello che sta ancora nella pagina della mappa va nella nota.</summary>
    private async void LockNow()
    {
        if (Page("map") is MapPage map)
        {
            await map.Flush();
        }

        _ws.Lock("a mano");
    }

    // ---------------------------------------------------------------- la mappa

    /// <summary>Il disegno a tratto della voce Mappa (lo stesso glifo dell'app per Mac).</summary>
    private const string MapGlyph = "M2.5,2.5 H6 V6 H2.5 Z M10,2.5 H13.5 V6 H10 Z M6.25,10 H9.75 V13.5 H6.25 Z M6,4.25 H10 M4.25,6 L7,10 M11.75,6 L9,10";

    /// <summary>L'ultima scorciatoia della finestra e quando: la mappa a volte la ripassa, e non deve valere due volte.</summary>
    private (string Key, DateTime At) _lastShortcut = (string.Empty, DateTime.MinValue);

    private NetworkPage CreateNetwork()
    {
        NetworkPage network = new(_ws);
        network.ReminderWanted += Compose;
        network.MapWanted += host =>
        {
            Navigate("map");
            (Page("map") as MapPage)?.Reveal(host);
        };
        return network;
    }

    private MapPage CreateMap()
    {
        MapPage map = new(_ws);
        map.WindowKey += OnMapKey;
        map.ReminderWanted += Compose;
        map.NetworkWanted += host =>
        {
            Navigate("network");

            if (host is not null && Page("network") is NetworkPage network)
            {
                network.Reveal(host);
            }
        };
        return map;
    }

    /// <summary>I tasti della finestra premuti mentre si lavora sulla mappa (dal WebView non arrivano da soli).</summary>
    private void OnMapKey(string key)
    {
        if (_lastShortcut.Key == key && DateTime.UtcNow - _lastShortcut.At < TimeSpan.FromMilliseconds(400))
        {
            return;
        }

        switch (key)
        {
            case "sidebar" when !_ws.IsLocked:
                ToggleSidebar();
                break;
            case "lock" when _ws.Vault.IsOpen:
                LockNow();
                break;
            case "settings" when !_ws.IsLocked:
                Navigate(_current == "settings" ? _beforeSettings : "settings");
                break;
            case "searchSheets" when !_ws.IsLocked:
                Navigate("notes");
                break;
        }
    }

    /// <summary>Accanto a Mappa: quanti apparati ci sono disegnati.</summary>
    // ---------------------------------------------------------------- il calendario

    private CalendarPage CreateCalendar()
    {
        CalendarPage calendar = new(_ws);
        calendar.WindowKey += OnMapKey;
        calendar.HostWanted += host =>
        {
            Navigate("network");
            (Page("network") as NetworkPage)?.Reveal(host);
        };
        calendar.DeviceWanted += device =>
        {
            Navigate("map");
            (Page("map") as MapPage)?.Reveal(device);
        };
        return calendar;
    }

    /// <summary>Il numero accanto a Calendario: le voci di oggi e quelle rimaste indietro (rosso se ce n'e').</summary>
    private void ShowCalendarCount()
    {
        if (_ws.IsLocked || _ws.IsDamaged)
        {
            SetBadge("calendar", 0, "Orange");
            SetBadge("calendar", 0, "Red");
            return;
        }

        AgendaSummary summary = _ws.Calendar.Summary(DateTime.Now);
        int count = summary.Today.Count + summary.Overdue.Count;
        bool late = summary.Overdue.Count > 0;
        string tip = late
            ? $"Oggi {summary.Today.Count}, in ritardo {summary.Overdue.Count}"
            : summary.Today.Count == 1 ? "1 voce oggi" : $"{summary.Today.Count} voci oggi";
        SetBadge("calendar", late ? count : 0, "Red", tip);
        SetBadge("calendar", late ? 0 : count, "Orange", tip);
    }

    /// <summary>Una voce nuova chiesta da Rete o dalla mappa, gia' legata all'host o all'apparato.</summary>
    private void Compose(object seed)
    {
        if (_ws.IsLocked)
        {
            return;
        }

        Navigate("calendar");
        (Page("calendar") as CalendarPage)?.Compose(seed);
    }

    private void ShowOccurrence(Occurrence occurrence)
    {
        Navigate("calendar");
        (Page("calendar") as CalendarPage)?.Show(occurrence.Date, occurrence.Key);
    }

    /// <summary>
    /// I promemoria scattati: un avviso ciascuno, che resta finche' non si sceglie (Fatto,
    /// Rimanda, Apri). Se DuckNote e' dietro, la barra delle applicazioni lampeggia.
    /// </summary>
    private void Remind(IReadOnlyList<AgendaAlert> alerts)
    {
        if (_ws.IsLocked || alerts.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.Now;

        if (alerts.Count > 3)
        {
            Toast.Show("ChipOrange", "Orange", "\uEA8F", $"{alerts.Count} promemoria",
                string.Join(", ", alerts.Take(3).Select(alert => AgendaText.Alert(alert, now).Title)) + " e altri.",
                () => Navigate("calendar"), sticky: true);
        }
        else
        {
            foreach (AgendaAlert alert in alerts)
            {
                Occurrence occurrence = alert.Occurrence;
                (string title, string text) = AgendaText.Alert(alert, now);
                string tone = occurrence.Item.Certificate ? "Orange" : occurrence.Item.Kind switch
                {
                    AgendaKinds.Todo => "Green",
                    AgendaKinds.Deadline => "Red",
                    _ => "Blue"
                };

                // un clic apre la voce nel calendario; Fatto e Rimanda col tasto destro
                Toast.Show("Chip" + tone, tone, "\uEA8F", title, text, () => ShowOccurrence(occurrence), sticky: true, more:
                [
                    new ToastAction("Fatto", () => _ws.MarkDone(occurrence)),
                    new ToastAction("Rimanda di 10 minuti", () => _ws.Snooze(occurrence, DateTime.Now.AddMinutes(10))),
                    new ToastAction("Rimanda di un'ora", () => _ws.Snooze(occurrence, DateTime.Now.AddHours(1))),
                    new ToastAction("Rimanda a domani alle 9", () => _ws.Snooze(occurrence, DateTime.Today.AddDays(1).AddHours(9)))
                ]);
            }
        }

        Attention.Request(this, insist: true);
    }

    /// <summary>All'apertura della nota: cosa c'e' oggi, cosa e' rimasto indietro, cosa arriva.</summary>
    private void Summarize(AgendaSummary summary)
    {
        ShowCalendarCount();

        if (summary.Today.Count == 0 && summary.Overdue.Count == 0 && summary.Certificates.Count == 0)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        string tone = summary.Overdue.Count > 0 ? "Red" : "Orange";

        // un attimo dopo l'apertura: prima la finestra (o lo sblocco), poi il riepilogo
        DispatcherTimer later = new() { Interval = TimeSpan.FromSeconds(1.2) };
        later.Tick += (_, _) =>
        {
            later.Stop();

            if (_ws.IsLocked)
            {
                return;
            }

            Toast.Show("Chip" + tone, tone, "\uE787", AgendaText.SummaryTitle(summary), AgendaText.SummaryText(summary, today),
                () => Navigate("calendar"));
            Attention.Request(this, insist: false);
        };
        later.Start();
    }

    private void ShowMapCount()
    {
        int devices = _ws.IsLocked ? 0 : DuckNote.Notes.Maps.NetworkMap.Devices(_ws.Book.Map);
        SetBadge("map", devices, "Gray", devices == 1 ? "1 apparato sulla mappa" : $"{devices} apparati sulla mappa");
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
        (Page("map") as MapPage)?.Forget();
        (Page("calendar") as CalendarPage)?.Forget();
        SetBadge("calendar", 0, "Orange");
        SetBadge("calendar", 0, "Red");

        // affacciata non resta: a nota riaperta la si ritrova nascosta com'era
        if (_side == Side.Peek)
        {
            SetSide(Side.Hidden, animate: false);
        }

        Lock.Present();
        ShowVault();
        ShowSaved();
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

        // a nota bloccata restano solo versione e giorno/notte (l'ingranaggio porterebbe a una
        // pagina che sta dietro alla schermata di sblocco)
        BtnLock.Visibility = BtnSidebar.Visibility = BtnSettings.Visibility = _ws.IsLocked ? Visibility.Collapsed : Visibility.Visible;
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

        // gli host che il monitoraggio controlla davvero: quelli dei gruppi col loro interruttore acceso
        int hosts = _ws.MonitoredCount;

        SetStatusCard(
            "Monitoraggio",
            _ws.Settings.MonitorEnabled
                ? string.Create(CultureInfo.CurrentCulture, $"{hosts} host controllati, ogni {_ws.Settings.MonitorIntervalSec} s. {session.UpCount} attivi, {session.DownCount} spenti.")
                : "Spento: gli host si controllano solo a mano.",
            null,
            _ws.Settings.MonitorEnabled ? (session.DownCount > 0 ? "Orange" : "Green") : "Gray",
            _ws.Settings.MonitorEnabled ? string.Create(CultureInfo.CurrentCulture, $"{_ws.Settings.MonitorIntervalSec}s") : "fermo");
    }

    private void Announce(string message)
    {
        if (message.StartsWith("Analisi interrotta", StringComparison.Ordinal))
        {
            Toast.Error("Analisi non riuscita", message, () => Navigate("network"), tab: "Rete");
        }
        else if (message.StartsWith("Fermata", StringComparison.Ordinal))
        {
            Toast.Info("Analisi fermata", message, () => Navigate("network"), tab: "Rete");
        }
        else if (!message.StartsWith("Indica", StringComparison.Ordinal) && !message.StartsWith("Niente", StringComparison.Ordinal))
        {
            Toast.Success("Analisi finita", message, () => Navigate("network"), tab: "Rete");
        }
    }

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        _ws.Activity();
        StatusOnKey(e);

        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control && _ws.Vault.IsOpen)
        {
            _lastShortcut = ("lock", DateTime.UtcNow);
            LockNow();
            e.Handled = true;
            return;
        }

        if (_ws.IsLocked)
        {
            return;
        }

        // la sidebar: F9, oppure Ctrl+\ come negli editor (il tasto accanto all'1 sulla tastiera italiana)
        if ((e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.None) || (e.Key == Key.Oem5 && Keyboard.Modifiers == ModifierKeys.Control))
        {
            _lastShortcut = ("sidebar", DateTime.UtcNow);
            ToggleSidebar();
            e.Handled = true;
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
