using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DuckNote.App.Dialogs;
using DuckNote.App.Scanning;
using DuckNote.App.Ui;
using DuckNote.App.Views;
using DuckNote.Notes.Groups;
using DuckNote.Notes.Scanning;
using Microsoft.Win32;

namespace DuckNote.App.Pages;

/// <summary>
/// La rete: scansione per intervallo o degli host della nota, contatori, filtro, tabella e la
/// scheda dell'host scelto, che entra da destra. Un clic sulla stessa riga la richiude.
/// </summary>
public partial class NetworkPage : UserControl, IDuckPage
{
    private static readonly string[] CsvHeader =
        ["Indirizzo", "Stato", "Nome", "NetBIOS", "mDNS", "MAC", "Produttore", "Risposta", "TTL", "Sistema", "Tipo", "Porte aperte", "Servizi", "Titolo web", "Ultima verifica"];

    /// <summary>Il filtro per gruppo: tutti gli host, o quelli fuori dalla nota e dai gruppi.</summary>
    private const string AllGroups = "*";
    private const string NoGroup = "-";

    private readonly Workspace _ws;
    private string _group = AllGroups;
    /// <summary>
    /// Le righe mostrate: un elenco tutto della tabella, rifatto dai filtri a ogni aggiornamento
    /// (al passo di _refresh), con le stesse righe della sessione. Niente vista filtrata
    /// condivisa sopra la collezione che la scansione sta riempiendo.
    /// </summary>
    private readonly ObservableCollection<HostRow> _visible = [];
    private HostQuery _query = HostQuery.Parse(null);
    private readonly System.Windows.Threading.DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private HostRow? _shown;

    private void Later()
    {
        if (!_refresh.IsEnabled)
        {
            _refresh.Start();
        }
    }

    public NetworkPage(Workspace ws)
    {
        _ws = ws;
        InitializeComponent();

        Table.ItemsSource = _visible;
        Table.SizeChanged += (_, change) =>
        {
            if (change.WidthChanged)
            {
                Dispatcher.BeginInvoke(SpreadColumns, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
        RangeBox.Text = ws.Settings.LastRange;

        // durante una scansione arrivano decine di esiti al secondo: la tabella si rinfresca a
        // passo d'uomo, non a ogni risultato
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            ShowSession();
        };

        ws.Session.PropertyChanged += (_, _) => Later();
        ws.HostsChanged += Later;
        ws.GroupsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            ShowGroup();
            Later();
        });
        ShowGroup();
        ShowSession();
    }

    /// <summary>
    /// Le colonne a larghezza proporzionale si misurano quando la tabella ha gia' il suo spazio:
    /// una pagina preparata fuori schermo le lascerebbe strette al minimo, con gli indirizzi
    /// tagliati. Le si riassegna quando la tabella cambia misura.
    /// </summary>
    private void SpreadColumns()
    {
        foreach (DataGridColumn column in Table.Columns)
        {
            DataGridLength width = column.Width;

            if (width.IsStar)
            {
                column.Width = new DataGridLength(width.Value, DataGridLengthUnitType.Pixel);
                column.Width = new DataGridLength(width.Value, DataGridLengthUnitType.Star);
            }
        }
    }

    public void OnShown()
    {
        SpreadColumns();
        ShowSession(force: true);
        RangeBox.Focus();
    }

    /// <summary>Apre la scheda di un host (chiesta dalla nota o dall'elenco host).</summary>
    public void Reveal(string host)
    {
        if (_ws.Session.Find(host) is not { } row)
        {
            _ws.Say($"Di {host} non c'e' ancora nulla da mostrare.");
            return;
        }

        if (!_visible.Contains(row))
        {
            FilterBox.Text = string.Empty;
            _group = AllGroups;
            ShowGroup();
            ShowAll.IsChecked = true;
            Fill();
        }

        Table.SelectedItem = row;
        Table.ScrollIntoView(row);
        Show(row);
    }

    private bool Wanted(HostRow row)
    {
        bool state = ShowUp?.IsChecked == true ? row.IsUp
            : ShowDown?.IsChecked == true ? row.IsDown
            : ShowWait?.IsChecked != true || row.NeedsCheck;

        return state && InGroup(row) && _query.Matches(row.Facts());
    }

    private bool InGroup(HostRow row)
    {
        GroupHosts groups = _ws.Groups;

        return _group switch
        {
            AllGroups => true,
            ScanGroups.Note => row.FromNote,
            NoGroup => !row.FromNote && !groups.InAny(row),
            _ => groups.Find(_group) is { } group && groups.Contains(group, row)
        };
    }

    /// <summary>
    /// Allinea le righe mostrate ai filtri toccando solo quelle che cambiano, in un passaggio
    /// solo: via chi non c'entra piu', dentro i nuovi al loro posto. Se l'ordine e' cambiato
    /// (raro: un nome che diventa un IP) l'elenco si rifa' da capo, tenendo la riga scelta.
    /// </summary>
    private void Fill()
    {
        List<HostRow> wanted = [.. _ws.Session.Rows.Where(Wanted)];
        HashSet<HostRow> keep = [.. wanted];

        for (int i = _visible.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_visible[i]))
            {
                _visible.RemoveAt(i);
            }
        }

        HashSet<HostRow> shown = [.. _visible];

        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < _visible.Count && ReferenceEquals(_visible[i], wanted[i]))
            {
                continue;
            }

            if (shown.Contains(wanted[i]))
            {
                HostRow? chosen = Table.SelectedItem as HostRow;
                _visible.Clear();

                foreach (HostRow row in wanted)
                {
                    _visible.Add(row);
                }

                if (chosen is not null && keep.Contains(chosen))
                {
                    Table.SelectedItem = chosen;
                }

                return;
            }

            _visible.Insert(i, wanted[i]);
            shown.Add(wanted[i]);
        }
    }

    private void ShowSession(bool force = false)
    {
        // una pagina che non si vede non si aggiorna: lo fa OnShown quando torna in primo piano
        if (!force && !IsVisible)
        {
            return;
        }

        ScanSession session = _ws.Session;

        CountAll.Text = session.Rows.Count.ToString(CultureInfo.CurrentCulture);
        CountUp.Text = session.UpCount.ToString(CultureInfo.CurrentCulture);
        CountDown.Text = session.DownCount.ToString(CultureInfo.CurrentCulture);
        CountWait.Text = session.WaitingCount.ToString(CultureInfo.CurrentCulture);
        HiddenText.Text = session.Hidden.Count == 0
            ? "Messi da parte"
            : string.Create(CultureInfo.CurrentCulture, $"Messi da parte ({session.Hidden.Count})");

        int fromNote = session.Rows.Count(row => row.FromNote);
        Summary.Text = session.Rows.Count == 0
            ? "Nessun host ancora"
            : string.Create(CultureInfo.CurrentCulture, $"{session.Rows.Count} host, {fromNote} dalla nota · {session.UpCount} attivi");

        BtnScan.IsEnabled = !session.IsRunning;
        BtnNote.IsEnabled = !session.IsRunning;
        BtnStop.IsEnabled = session.IsRunning;
        RangeBox.IsEnabled = !session.IsRunning;
        Progress.Visibility = session.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = session.Progress;

        Fill();

        int shown = _visible.Count;
        Empty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = session.Rows.Count == 0 ? "Nessun host da mostrare" : "Nessun host corrisponde ai filtri";
        EmptyText.Text = session.Rows.Count == 0
            ? "Scrivi un intervallo e premi Analizza, oppure scrivi degli indirizzi in una pagina della nota."
            : "Cambia filtro o ricerca per vederne altri.";

        Footer.Text = session.IsRunning
            ? session.Message
            : string.Create(CultureInfo.CurrentCulture, $"{shown} di {session.Rows.Count} mostrati · {session.Message}");

        if (_shown is not null && !session.Rows.Contains(_shown))
        {
            Show(null);
        }
        else if (_shown is not null)
        {
            Fill(_shown);
        }
    }

    private async void OnScanRange(object sender, RoutedEventArgs e)
    {
        if (RangeBox.Text.Trim().Length == 0)
        {
            Fx.Shake(RangeBox);
            _ws.Say("Indica un intervallo, per esempio 192.168.1.0/24");
            return;
        }

        await _ws.ScanRange(RangeBox.Text.Trim());
    }

    private async void OnRangeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_ws.Session.IsRunning)
        {
            e.Handled = true;
            await _ws.ScanRange(RangeBox.Text.Trim());
        }
    }

    private async void OnScanNote(object sender, RoutedEventArgs e) => await _ws.ScanGroup(_ws.Groups.Find(_group)?.Id ?? ScanGroups.Note);

    private void OnStop(object sender, RoutedEventArgs e) => _ws.Session.Stop();

    private void OnFilter(object sender, RoutedEventArgs e)
    {
        // parte anche durante InitializeComponent (radio preselezionato): la tabella non c'e' ancora
        if (Table is null || FilterBox is null)
        {
            return;
        }

        _query = HostQuery.Parse(FilterBox.Text);
        ShowSession();
    }

    // ------------------------------------------------------------------ scheda

    private void OnPick(object sender, SelectionChangedEventArgs e) => Show(Table.SelectedItem as HostRow);

    private void Show(HostRow? row)
    {
        _shown = row;
        CardEmpty.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        Card.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;

        if (row is not null)
        {
            Fill(row);
            Fx.Entrance(Card);
        }
    }

    private void Fill(HostRow row)
    {
        CardTitle.Text = row.Name.Length > 0 ? row.Name : row.IP;
        // l'IP sotto al titolo solo se il titolo e' un nome: due volte lo stesso numero non serve
        CardSub.Text = string.Join("  ·  ", ((string[])[row.Name.Length > 0 ? row.IP : string.Empty, row.DeviceType]).Where(part => part.Length > 0));
        CardSub.Visibility = CardSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CardStatus.Text = row.Status.ToUpperInvariant();
        Tok.SetForeground(CardDot, row.Tone);
        CardChip.Style = (Style)FindResource(row.Chip);
        CardStatus.SetResourceReference(TextBlock.ForegroundProperty, row.Tone);

        CardBody.Children.Clear();

        Section("Identita'",
            ("Indirizzo IP", row.IP), ("Nella nota come", row.NoteKey == row.IP ? string.Empty : row.NoteKey),
            ("Nome DNS", row.Hostname), ("Nome NetBIOS", row.NetBiosName), ("Nome mDNS", row.MdnsName),
            ("Gruppo/Dominio", First(row.Workgroup, row.Domain)), ("Indirizzo MAC", row.Mac), ("Produttore", row.Vendor),
            ("Utente", row.LoggedUser));
        Section("Raggiungibilita'",
            ("Stato", row.Status), ("Latenza", row.RoundTrip), ("Perdita", row.Loss), ("TTL", row.Ttl),
            ("Ultima volta attivo", row.LastSeen), ("Durata analisi", row.ScanMs > 0 ? $"{row.ScanMs} ms" : string.Empty));
        Section("Sistema",
            ("Stima OS", row.Os), ("Tipo", row.DeviceType), ("Sistema (WMI)", row.WmiOs), ("Modello", row.WmiModel),
            ("Seriale", row.WmiSerial), ("CPU", row.WmiCpu), ("Memoria", row.WmiRam), ("Dischi", row.WmiDisks), ("Uptime", row.WmiUptime));
        Section("Servizi",
            ("Porte aperte", row.OpenPorts), ("Servizi", row.Services), ("Condivisioni", row.Shares), ("RDP", row.RdpInfo));
        Section("Applicazioni",
            ("Titolo web", row.HttpTitle), ("Server HTTP", row.HttpServer), ("SSH", row.SshBanner), ("FTP", row.FtpBanner),
            ("SMTP", row.SmtpBanner), ("UPnP", string.Join(" / ", ((string[])[row.UpnpDevice, row.UpnpServer]).Where(p => p.Length > 0))));
        Section("Certificato TLS", ("Soggetto", row.TlsSubject), ("Emittente", row.TlsIssuer), ("Scadenza", row.TlsExpiry));
        Section("SNMP",
            ("Nome", row.SnmpName), ("Descrizione", row.SnmpDescr), ("Posizione", row.SnmpLocation),
            ("Contatto", row.SnmpContact), ("Uptime", row.SnmpUptime));

        if (row.Findings.Length > 0)
        {
            Border note = new() { Style = (Style)FindResource("NoteBox"), Margin = new Thickness(0, 14, 0, 0) };
            TextBlock text = new() { Text = row.Findings, TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Label");
            note.Child = text;
            CardBody.Children.Add(note);
        }

        if (!row.IsChecked)
        {
            TextBlock wait = new() { Text = "Non ancora analizzato: il controllo parte da solo fra poco.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
            wait.Style = (Style)FindResource("TxtMuted");
            CardBody.Children.Add(wait);
        }
    }

    private void Section(string title, params (string Label, string? Value)[] lines)
    {
        (string Label, string Value)[] shown = [.. lines.Where(line => !string.IsNullOrWhiteSpace(line.Value)).Select(line => (line.Label, line.Value!))];

        if (shown.Length == 0)
        {
            return;
        }

        CardBody.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Style = (Style)FindResource("GroupHeader"),
            Margin = new Thickness(0, 16, 0, 6)
        });

        foreach ((string label, string value) in shown)
        {
            Grid line = new() { Margin = new Thickness(0, 0, 0, 5) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            line.ColumnDefinitions.Add(new ColumnDefinition());

            TextBlock name = new() { Text = label, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("TxtCaption") };
            TextBox content = new() { Text = value, Style = (Style)FindResource("ReadOnlyValue"), FontSize = 11.5 };
            Grid.SetColumn(content, 1);

            line.Children.Add(name);
            line.Children.Add(content);
            CardBody.Children.Add(line);
        }
    }

    private static string First(params string[] candidates) => candidates.FirstOrDefault(c => c.Length > 0) ?? string.Empty;

    // ------------------------------------------------------------------ menu, esportazione

    private HostRow? Chosen => Table.SelectedItem as HostRow;

    private void OnRowMenu(object sender, RoutedEventArgs e)
    {
        bool any = Chosen is not null;
        RowAnalyse.IsEnabled = any && !_ws.Session.IsRunning;
        RowCopy.IsEnabled = any;
        RowRemind.IsEnabled = any && !_ws.IsLocked;
        RowHide.IsEnabled = any;
        RowHide.Header = Chosen is { } row ? $"Metti da parte {row.Shown}" : "Metti da parte";

        // chi e' stato messo da parte non e' piu' in tabella: si riprende da qui, col tasto destro
        RowHiddenList.Items.Clear();

        foreach (string host in _ws.Session.Hidden.ToList())
        {
            MenuItem item = new() { Header = $"Rimetti in lista {host}" };
            item.Click += (_, _) => _ws.Show(host);
            RowHiddenList.Items.Add(item);
        }

        RowHiddenList.Visibility = RowHiddenList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRowAnalyse(object sender, RoutedEventArgs e)
    {
        if (Chosen is { } row)
        {
            await _ws.Analyse(row.NoteKey.Length > 0 ? row.NoteKey : row.IP);
        }
    }

    /// <summary>"Aggiungi un promemoria" su un host: una voce nuova del calendario legata a lui.</summary>
    public event Action<object>? ReminderWanted;

    private void OnRowRemind(object sender, RoutedEventArgs e)
    {
        if (Chosen is not { } row)
        {
            return;
        }

        Dictionary<string, object?> seed = new()
        {
            ["kind"] = "event",
            ["link"] = new Dictionary<string, string> { ["kind"] = "host", ["host"] = row.Shown, ["name"] = row.Label == row.Shown ? string.Empty : row.Label }
        };

        // con un certificato che scade, la voce e' gia' pronta come scadenza una settimana prima
        if (DuckNote.Notes.Calendar.Agenda.Day(row.TlsExpiry) is { } expires && expires >= DateOnly.FromDateTime(DateTime.Now))
        {
            DateOnly before = expires.AddDays(-7);
            seed["kind"] = "deadline";
            seed["title"] = $"Rinnovare il certificato di {row.Shown}";
            seed["date"] = DuckNote.Notes.Calendar.Agenda.Iso(before >= DateOnly.FromDateTime(DateTime.Now) ? before : expires);
        }

        ReminderWanted?.Invoke(seed);
    }

    private void OnRowCopy(object sender, RoutedEventArgs e)
    {
        if (Chosen is not { } row)
        {
            return;
        }

        try
        {
            Clipboard.SetText(row.IP);
            _ws.Say($"{row.IP} copiato.");
        }
        catch (ExternalException)
        {
            _ws.Say("Gli appunti sono occupati da un altro programma: riprova.");
        }
    }

    private void OnRowHide(object sender, RoutedEventArgs e)
    {
        if (Chosen is { } row)
        {
            _ws.Hide(row.NoteKey.Length > 0 ? row.NoteKey : row.IP);
        }
    }

    /// <summary>Il menu degli host messi da parte: uno per riga, un clic li riprende.</summary>
    private void OnHiddenMenu(object sender, RoutedEventArgs e)
    {
        HiddenMenu.Items.Clear();

        if (_ws.Session.Hidden.Count == 0)
        {
            HiddenMenu.Items.Add(new MenuItem
            {
                Header = "Nessuno. Si mette da parte col tasto destro, nella nota o qui.",
                IsEnabled = false
            });
        }

        foreach (string host in _ws.Session.Hidden.ToList())
        {
            MenuItem item = new() { Header = $"Riprendi {host}", ToolTip = "Torna ad analizzarlo" };
            item.Click += (_, _) => _ws.Show(host);
            HiddenMenu.Items.Add(item);
        }

        HiddenMenu.PlacementTarget = BtnHidden;
        HiddenMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        HiddenMenu.IsOpen = true;
    }

    // ------------------------------------------------------------------ gruppi

    /// <summary>Il nome del gruppo scelto sul pulsante, e il secondo comando che analizza lui (o la nota).</summary>
    private void ShowGroup()
    {
        ScanGroup? group = _ws.Groups.Find(_group);

        if (group is null && _group is not (AllGroups or NoGroup or ScanGroups.Note))
        {
            _group = AllGroups;
        }

        GroupText.Text = _group switch
        {
            AllGroups => "Tutti i gruppi",
            ScanGroups.Note => "Nota",
            NoGroup => "Ricerca",
            _ => group!.Name
        };

        NoteIcon.Text = group is null ? "\uE8A5" : "\uE81E";
        NoteText.Text = group is null ? "Host della nota" : $"Analizza «{Short(group.Name, 22)}»";
        BtnNote.ToolTip = group is null
            ? "Solo gli indirizzi scritti nelle pagine della nota"
            : group.Targets.Count == 0 ? "Il gruppo e' vuoto: aggiungi reti o indirizzi con Modifica" : $"Analizza {Short(string.Join(", ", group.Targets), 120)}";
    }

    private void OnGroupMenu(object sender, RoutedEventArgs e)
    {
        GroupHosts groups = _ws.Groups;
        IReadOnlyList<HostRow> rows = _ws.Session.Rows;

        GroupMenu.Items.Clear();
        GroupMenu.Items.Add(GroupEntry(AllGroups, "Tutti i gruppi", rows.Count, "Gli host della nota, dei gruppi e della ricerca"));
        GroupMenu.Items.Add(GroupEntry(ScanGroups.Note, "Nota", rows.Count(row => row.FromNote), "Gli indirizzi scritti nei fogli e sulla mappa"));

        if (groups.All.Count > 0)
        {
            GroupMenu.Items.Add(new Separator());
        }

        foreach (ScanGroup group in groups.All)
        {
            GroupMenu.Items.Add(GroupEntry(group.Id, group.Name, groups.Count(group, rows),
                group.Targets.Count == 0 ? "Vuoto" : Short(string.Join(", ", group.Targets), 120), group.Monitor));
        }

        GroupMenu.Items.Add(new Separator());
        GroupMenu.Items.Add(GroupEntry(NoGroup, "Ricerca", rows.Count(row => !row.FromNote && !groups.InAny(row)), "Trovati per intervallo, fuori dalla nota e dai gruppi"));
        GroupMenu.Items.Add(new Separator());
        GroupMenu.Items.Add(Command("Nuovo gruppo...", "\uE710", "Accent", () => EditGroup(null)));

        if (groups.Find(_group) is { } chosen)
        {
            GroupMenu.Items.Add(Command($"Modifica «{Short(chosen.Name, 30)}»...", "\uE70F", null, () => EditGroup(chosen)));
            GroupMenu.Items.Add(Command($"Elimina «{Short(chosen.Name, 30)}»", "\uE74D", "Red", () => RemoveGroup(chosen)));
        }

        GroupMenu.PlacementTarget = BtnGroup;
        GroupMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        GroupMenu.IsOpen = true;
    }

    private MenuItem GroupEntry(string id, string name, int count, string tip, bool monitored = false)
    {
        StackPanel title = new() { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = name, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

        if (monitored)
        {
            TextBlock pulse = Glyph("\uE9D9", "Green");
            pulse.Margin = new Thickness(7, 0, 0, 0);
            pulse.ToolTip = "Monitorato: i suoi host si controllano da soli";
            title.Children.Add(pulse);
        }

        TextBlock number = new() { Text = count.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
        Grid.SetColumn(number, 1);

        Grid header = new() { MinWidth = 210 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        header.Children.Add(number);

        MenuItem item = new() { Header = header, ToolTip = tip, Icon = id == _group ? Glyph("\uE73E", "Accent") : null };
        item.Click += (_, _) =>
        {
            _group = id;
            ShowGroup();
            ShowSession();
        };
        return item;
    }

    private static MenuItem Command(string header, string icon, string? tone, Action run)
    {
        MenuItem item = new() { Header = header, Icon = Glyph(icon, tone) };
        item.Click += (_, _) => run();
        return item;
    }

    private static TextBlock Glyph(string icon, string? tone)
    {
        TextBlock glyph = new() { Text = icon, FontSize = 12 };
        glyph.SetResourceReference(StyleProperty, "Icon");

        if (tone is not null)
        {
            glyph.SetResourceReference(TextBlock.ForegroundProperty, tone);
        }

        return glyph;
    }

    /// <summary>Crea (con <paramref name="group"/> null) o modifica un gruppo; il nuovo diventa quello scelto.</summary>
    private void EditGroup(ScanGroup? group)
    {
        List<ScanGroup> all = [.. _ws.Groups.All];
        string suggested = group is null ? RangeBox.Text.Trim() : string.Empty;

        if (GroupDialog.Ask(Window.GetWindow(this), group, all, suggested) is not { } choice)
        {
            return;
        }

        if (choice.Removed)
        {
            all.RemoveAll(other => other.Id == choice.Group.Id);
            _group = AllGroups;
            _ws.SaveGroups(all);
            _ws.Say($"Gruppo «{choice.Group.Name}» eliminato: gli host trovati restano in tabella.");
            return;
        }

        int at = all.FindIndex(other => other.Id == choice.Group.Id);

        if (at >= 0)
        {
            all[at] = choice.Group;
        }
        else
        {
            all.Add(choice.Group);
        }

        _group = choice.Group.Id;
        _ws.SaveGroups(all);
        _ws.Say(at >= 0 ? $"Gruppo «{choice.Group.Name}» aggiornato." : $"Gruppo «{choice.Group.Name}» creato: Analizza lo controlla tutto.");
    }

    private void RemoveGroup(ScanGroup group)
    {
        if (!DuckDialog.Confirm(Window.GetWindow(this), $"Eliminare «{group.Name}»?",
                "Il gruppo sparisce dalla nota e dalla mappa; gli host gia' trovati restano in tabella.", "Elimina", danger: true))
        {
            return;
        }

        _group = AllGroups;
        _ws.SaveGroups([.. _ws.Groups.All.Where(other => other.Id != group.Id)]);
        _ws.Say($"Gruppo «{group.Name}» eliminato.");
    }

    private static string Short(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";

    private List<IReadOnlyList<string>> ShownRows() =>
    [
        .. Table.Items.OfType<HostRow>().Select(row => (IReadOnlyList<string>)
        [
            row.IP, row.Status, row.Hostname, row.NetBiosName, row.MdnsName, row.Mac, row.Vendor, row.RoundTrip,
            row.Ttl, row.Os, row.DeviceType, row.OpenPorts, row.Services, row.HttpTitle, row.LastSeen
        ])
    ];

    private void OnExport(object sender, RoutedEventArgs e)
    {
        List<IReadOnlyList<string>> rows = ShownRows();

        if (rows.Count == 0)
        {
            _ws.Say("Niente da esportare: la tabella e' vuota.");
            return;
        }

        SaveFileDialog pick = new()
        {
            Title = "Esporta gli host",
            Filter = "CSV (separato da punto e virgola)|*.csv",
            FileName = $"ducknote-host-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (pick.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(pick.FileName, CsvTable.Write(CsvHeader, rows));
            Toast.Success("Esportazione fatta", $"{rows.Count} host in {Path.GetFileName(pick.FileName)}.");
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            Toast.Error("Esportazione non riuscita", problem.Message);
        }
    }

    private void OnToNote(object sender, RoutedEventArgs e)
    {
        List<IReadOnlyList<string>> rows =
        [
            .. Table.Items.OfType<HostRow>().Select(row => (IReadOnlyList<string>)[row.IP, row.Name, row.Status, row.Os, row.OpenPorts])
        ];

        if (rows.Count == 0)
        {
            _ws.Say("Niente da portare nella nota: la tabella e' vuota.");
            return;
        }

        _ws.SendTable(["Indirizzo", "Nome", "Stato", "Sistema", "Porte aperte"], rows);
    }
}
