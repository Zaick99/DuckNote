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
using DuckNote.App.Scanning;
using DuckNote.App.Ui;
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

    private readonly Workspace _ws;
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
        ShowSession();
    }

    public void OnShown()
    {
        ShowSession();
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
            FromAll.IsChecked = true;
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
            : ShowWait?.IsChecked != true || !row.IsChecked;

        bool source = OnlyNote?.IsChecked == true ? row.FromNote
            : OnlySearch?.IsChecked != true || !row.FromNote;

        return state && source && _query.Matches(row.Facts());
    }

    /// <summary>Allinea le righe mostrate ai filtri, toccando solo quelle che cambiano.</summary>
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

        for (int i = 0; i < wanted.Count; i++)
        {
            int at = _visible.IndexOf(wanted[i]);

            if (at < 0)
            {
                _visible.Insert(i, wanted[i]);
            }
            else if (at != i)
            {
                _visible.Move(at, i);
            }
        }
    }

    private void ShowSession()
    {
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

    private async void OnScanNote(object sender, RoutedEventArgs e) => await _ws.ScanNote();

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
        CardSub.Text = string.Join("  ·  ", ((string[])[row.IP, row.DeviceType]).Where(part => part.Length > 0));
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
