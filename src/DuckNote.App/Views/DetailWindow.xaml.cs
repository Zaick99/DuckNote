using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuckNote.App.Models;

namespace DuckNote.App.Views;

public partial class DetailWindow : Window
{
    private const double LabelColumn = 104;

    public DetailWindow(ResourceDictionary ownerResources)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(ownerResources);

        DClose.Click += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        };

        DTitleBar.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    public event EventHandler? Dismissed;

    public void Fill(ScanRow? row)
    {
        DBody.Children.Clear();

        if (row is null)
        {
            DTitle.Text = "Nessuna selezione";
            DSub.Text = "Seleziona un host nella tabella.";
            DBadgeBox.Visibility = Visibility.Collapsed;
            return;
        }

        Headline(row);

        Section("Identita'");
        Row("Indirizzo IP", row.IP);
        Row("Nome DNS", row.Hostname);
        Row("Nome NetBIOS", row.NetBiosName);
        Row("Nome mDNS", row.MdnsName);
        Row("Gruppo/Dominio", First(row.Workgroup, row.Domain));
        Row("Indirizzo MAC", row.Mac);
        Row("Produttore", row.Vendor);
        Row("Utente", row.LoggedUser);

        Section("Raggiungibilita'");
        Row("Stato", row.Status);
        Row("Latenza", row.RttMs);
        Row("Perdita", row.Loss);
        Row("TTL", row.Ttl);
        Row("Ultima verifica", row.LastSeen);
        Row("Durata analisi", row.ScanMs > 0 ? $"{row.ScanMs} ms" : null);

        Section("Sistema");
        Row("Stima OS", row.OsGuess);
        Row("Tipo", row.DeviceType);
        Row("Sistema (WMI)", row.WmiOs);
        Row("Modello", row.WmiModel);
        Row("Seriale", row.WmiSerial);
        Row("CPU", row.WmiCpu);
        Row("Memoria", row.WmiRam);
        Row("Dischi", row.WmiDisks);
        Row("Uptime", row.WmiUptime);

        Section("Servizi");
        Row("Porte aperte", row.OpenPorts);
        Row("Servizi", row.Services);
        Row("Condivisioni", row.Shares);
        Row("RDP", row.RdpInfo);

        Section("Applicazioni");
        Row("Titolo web", row.HttpTitle);
        Row("Server HTTP", row.HttpServer);
        Row("SSH", row.SshBanner);
        Row("FTP", row.FtpBanner);
        Row("SMTP", row.SmtpBanner);
        Row("UPnP", Join(" / ", row.UpnpDevice, row.UpnpServer));

        Section("Certificato TLS");
        Row("Soggetto", row.TlsSubject);
        Row("Emittente", row.TlsIssuer);
        Row("Scadenza", row.TlsExpiry);

        Section("SNMP");
        Row("Nome", row.SnmpName);
        Row("Descrizione", row.SnmpDescr);
        Row("Posizione", row.SnmpLocation);
        Row("Contatto", row.SnmpContact);
        Row("Uptime", row.SnmpUptime);

        if (!string.IsNullOrWhiteSpace(row.Notes))
        {
            Section("Rilievi");
            Row("Note", row.Notes, "Orange");
        }

        PruneEmptySections();
    }

    private void Headline(ScanRow row)
    {
        DTitle.Text = First(row.Hostname, row.NetBiosName) ?? row.IP;
        DSub.Text = Join("  ·  ", row.IP, row.DeviceType) ?? string.Empty;

        DBadge.Text = row.Status?.ToUpperInvariant() ?? string.Empty;
        DBadgeBox.Visibility = string.IsNullOrWhiteSpace(row.Status) ? Visibility.Collapsed : Visibility.Visible;

        string tone = row.StatusRank <= 1 ? "Green" : row.StatusRank <= 3 ? "Red" : "LabelTertiary";
        DBadge.SetResourceReference(TextBlock.ForegroundProperty, tone);
        DBadgeBox.SetResourceReference(Border.BorderBrushProperty, tone);
    }

    private void Section(string title)
    {
        TextBlock heading = new()
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 6),
            Tag = "sezione"
        };

        heading.SetResourceReference(TextBlock.ForegroundProperty, "LabelSecondary");
        DBody.Children.Add(heading);
    }

    private void Row(string label, string? value, string? accent = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Grid line = new() { Margin = new Thickness(0, 0, 0, 5) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumn) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock name = new() { Text = label, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        name.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
        Grid.SetColumn(name, 0);

        TextBlock content = new() { Text = value, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        content.SetResourceReference(TextBlock.ForegroundProperty, accent ?? "Label");
        Grid.SetColumn(content, 1);

        line.Children.Add(name);
        line.Children.Add(content);
        DBody.Children.Add(line);
    }

    private void PruneEmptySections()
    {
        for (int i = DBody.Children.Count - 1; i >= 0; i--)
        {
            bool isHeading = DBody.Children[i] is TextBlock { Tag: "sezione" };
            bool nothingFollows = i == DBody.Children.Count - 1
                                  || DBody.Children[i + 1] is TextBlock { Tag: "sezione" };

            if (isHeading && nothingFollows)
            {
                DBody.Children.RemoveAt(i);
            }
        }
    }

    private static string? First(params string?[] candidates) =>
        candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? Join(string separator, params string?[] parts)
    {
        string joined = string.Join(separator, parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        return joined.Length == 0 ? null : joined;
    }
}
