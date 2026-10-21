using System.Globalization;
using System.Net;
using DuckNote.App.Ui;
using DuckNote.Scan;

namespace DuckNote.App.Scanning;

/// <summary>
/// Una riga della tabella degli host. Nasce da un indirizzo scritto nella nota (in attesa di
/// essere controllato) o da una scansione, e si aggiorna a ogni nuovo esito.
/// </summary>
public sealed class HostRow : Observable
{
    /// <summary>Mai controllato: sotto questo rango c'e' un esito vero.</summary>
    public const int NeverChecked = 4;

    public string IP { get; private set; } = string.Empty;

    /// <summary>L'indirizzo come e' scritto nella nota (un nome, magari): vuoto per le righe da intervallo.</summary>
    public string NoteKey { get; private set; } = string.Empty;

    public long SortKey { get; private set; } = long.MaxValue;
    public string Status { get; private set; } = "mai controllato";
    public int Rank { get; private set; } = NeverChecked;
    public string Hostname { get; private set; } = string.Empty;
    public string NetBiosName { get; private set; } = string.Empty;
    public string MdnsName { get; private set; } = string.Empty;
    public string Workgroup { get; private set; } = string.Empty;
    public string Domain { get; private set; } = string.Empty;
    public string Mac { get; private set; } = string.Empty;
    public string Vendor { get; private set; } = string.Empty;
    public string RoundTrip { get; private set; } = string.Empty;
    public double AverageMs { get; private set; } = double.MaxValue;
    public string Loss { get; private set; } = string.Empty;
    public string Ttl { get; private set; } = string.Empty;
    public string Os { get; private set; } = string.Empty;
    public string DeviceType { get; private set; } = string.Empty;
    public string OpenPorts { get; private set; } = string.Empty;
    public int PortCount { get; private set; }
    public string Services { get; private set; } = string.Empty;
    public string HttpTitle { get; private set; } = string.Empty;
    public string HttpServer { get; private set; } = string.Empty;
    public string TlsSubject { get; private set; } = string.Empty;
    public string TlsIssuer { get; private set; } = string.Empty;
    public string TlsExpiry { get; private set; } = string.Empty;
    public string SshBanner { get; private set; } = string.Empty;
    public string FtpBanner { get; private set; } = string.Empty;
    public string SmtpBanner { get; private set; } = string.Empty;
    public string RdpInfo { get; private set; } = string.Empty;
    public string SnmpName { get; private set; } = string.Empty;
    public string SnmpDescr { get; private set; } = string.Empty;
    public string SnmpLocation { get; private set; } = string.Empty;
    public string SnmpContact { get; private set; } = string.Empty;
    public string SnmpUptime { get; private set; } = string.Empty;
    public string UpnpDevice { get; private set; } = string.Empty;
    public string UpnpServer { get; private set; } = string.Empty;
    public string Shares { get; private set; } = string.Empty;
    public string LoggedUser { get; private set; } = string.Empty;
    public string WmiOs { get; private set; } = string.Empty;
    public string WmiModel { get; private set; } = string.Empty;
    public string WmiSerial { get; private set; } = string.Empty;
    public string WmiUptime { get; private set; } = string.Empty;
    public string WmiCpu { get; private set; } = string.Empty;
    public string WmiRam { get; private set; } = string.Empty;
    public string WmiDisks { get; private set; } = string.Empty;
    public string Findings { get; private set; } = string.Empty;
    public string LastSeen { get; private set; } = string.Empty;
    public int ScanMs { get; private set; }

    /// <summary>Come la tabella lo chiama: com'e' scritto nella nota, se viene da li', altrimenti l'indirizzo.</summary>
    public string Shown => NoteKey.Length > 0 ? NoteKey : IP;

    public bool FromNote => NoteKey.Length > 0;

    /// <summary>La colonna Indirizzo: sempre l'IP; finche' non si conosce, com'e' scritto nella nota.</summary>
    public string Address => IP.Length > 0 ? IP : NoteKey;

    /// <summary>
    /// La colonna Nome: quello trovato (DNS, NetBIOS, mDNS); se manca e nella nota l'host e'
    /// scritto per nome, quel nome.
    /// </summary>
    public string Label => Name.Length > 0 ? Name
        : NoteKey.Length > 0 && !System.Net.IPAddress.TryParse(NoteKey, out _) ? NoteKey : string.Empty;

    public System.Windows.Visibility NoteVis => FromNote ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public bool IsUp => Rank <= 1;

    public bool IsDown => Rank is > 1 and < NeverChecked;

    public bool IsChecked => Rank < NeverChecked;

    /// <summary>Il nome da mostrare: quello DNS, poi NetBIOS, poi mDNS.</summary>
    public string Name => First(Hostname, NetBiosName, MdnsName) ?? string.Empty;

    /// <summary>
    /// Il token del colore dello stato (pallino, chip): verde risponde (anche col ping filtrato),
    /// rosso spento, arancio da verificare (mai provato, o la prova e' andata in errore).
    /// </summary>
    public string Tone => Rank switch
    {
        0 or 1 => "Green",
        2 => "Red",
        _ => "Orange"
    };

    public string Chip => Rank switch
    {
        0 or 1 => "ChipGreen",
        2 => "ChipRed",
        _ => "ChipOrange"
    };

    /// <summary>Il pallino in parole, per il tooltip.</summary>
    public string ToneText => Rank switch
    {
        0 or 1 => "Online",
        2 => "Offline",
        _ => "Da verificare"
    };

    /// <summary>Lega una riga trovata per intervallo all'indirizzo scritto nella nota.</summary>
    public void Bind(string written)
    {
        NoteKey = written;
        RaiseAll();
    }

    public static HostRow Pending(string written) => new()
    {
        IP = written,
        NoteKey = written,
        SortKey = Sortable(written)
    };

    /// <summary>
    /// Il legame con la nota (NoteKey) non lo tocca: lo ha chi e' nato da un indirizzo scritto, e
    /// una riga nata da un intervallo non ce l'ha mai, cosi' resta anche quando la nota cambia.
    /// </summary>
    public void Apply(HostScanResult result)
    {
        IP = result.Address;
        SortKey = Sortable(result.Address);
        Status = result.StatusText;
        Rank = (int)result.Status;
        Hostname = result.Hostname;
        NetBiosName = result.NetBiosName;
        MdnsName = result.MdnsName;
        Workgroup = result.Workgroup;
        Domain = result.Domain;
        Mac = result.Mac;
        Vendor = result.Vendor;
        RoundTrip = result.RoundTrip;
        AverageMs = result.IsAlive ? result.AverageMs : double.MaxValue;
        Loss = result.Loss;
        Ttl = result.Ttl;
        Os = result.OperatingSystem;
        DeviceType = result.DeviceType;
        OpenPorts = string.Join(", ", result.OpenPorts);
        PortCount = result.OpenPorts.Count;
        Services = result.Services;
        HttpTitle = result.HttpTitle;
        HttpServer = result.HttpServer;
        TlsSubject = result.TlsSubject;
        TlsIssuer = result.TlsIssuer;
        TlsExpiry = result.TlsExpiry;
        SshBanner = result.SshBanner;
        FtpBanner = result.FtpBanner;
        SmtpBanner = result.SmtpBanner;
        RdpInfo = result.RdpInfo;
        SnmpName = result.SnmpName;
        SnmpDescr = result.SnmpDescription;
        SnmpLocation = result.SnmpLocation;
        SnmpContact = result.SnmpContact;
        SnmpUptime = result.SnmpUptime;
        UpnpDevice = result.UpnpDevice;
        UpnpServer = result.UpnpServer;
        Shares = result.Shares;
        LoggedUser = result.LoggedUser;
        WmiOs = result.WmiOs;
        WmiModel = result.WmiModel;
        WmiSerial = result.WmiSerial;
        WmiUptime = result.WmiUptime;
        WmiCpu = result.WmiCpu;
        WmiRam = result.WmiRam;
        WmiDisks = result.WmiDisks;
        Findings = result.Findings;
        ScanMs = result.ElapsedMs;

        if (result.IsAlive)
        {
            LastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        RaiseAll();
    }

    /// <summary>
    /// Un controllo leggero (solo ping): cambiano stato e tempi, tutto il resto raccolto prima
    /// resta com'era.
    /// </summary>
    public void Refresh(HostScanResult result)
    {
        Status = result.StatusText;
        Rank = (int)result.Status;
        Loss = result.Loss;

        if (result.IsAlive)
        {
            RoundTrip = result.RoundTrip;
            AverageMs = result.AverageMs;
            Ttl = result.Ttl.Length > 0 ? result.Ttl : Ttl;
            LastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            AverageMs = double.MaxValue;
        }

        RaiseAll();
    }

    /// <summary>Tutti i nomi con cui la nota o la tabella possono chiamare questo host.</summary>
    public IEnumerable<string> Names() =>
        ((string[])[IP, NoteKey, Hostname]).Where(name => name.Length > 0);

    /// <summary>I campi per il filtro della tabella (vedi HostQuery).</summary>
    public IReadOnlyDictionary<string, string> Facts() => new Dictionary<string, string>
    {
        ["ip"] = IP,
        ["nome"] = string.Join(' ', NoteKey, Hostname, NetBiosName, MdnsName),
        ["mac"] = Mac,
        ["porte"] = OpenPorts,
        ["os"] = string.Join(' ', Os, WmiOs),
        ["tipo"] = DeviceType,
        ["produttore"] = Vendor,
        ["gruppo"] = string.Join(' ', Workgroup, Domain),
        ["web"] = string.Join(' ', HttpTitle, HttpServer, TlsSubject),
        ["nota"] = Findings,
        ["stato"] = Status
    };

    // ----------------------------------------------------------- ultima scansione

    private static readonly string[] Stored =
    [
        nameof(IP), nameof(NoteKey), nameof(Status), nameof(Rank), nameof(Hostname), nameof(NetBiosName),
        nameof(MdnsName), nameof(Workgroup), nameof(Domain), nameof(Mac), nameof(Vendor), nameof(RoundTrip),
        nameof(Loss), nameof(Ttl), nameof(Os), nameof(DeviceType), nameof(OpenPorts), nameof(Services),
        nameof(HttpTitle), nameof(HttpServer), nameof(TlsSubject), nameof(TlsIssuer), nameof(TlsExpiry),
        nameof(SshBanner), nameof(FtpBanner), nameof(SmtpBanner), nameof(RdpInfo), nameof(SnmpName),
        nameof(SnmpDescr), nameof(SnmpLocation), nameof(SnmpContact), nameof(SnmpUptime), nameof(UpnpDevice),
        nameof(UpnpServer), nameof(Shares), nameof(LoggedUser), nameof(WmiOs), nameof(WmiModel), nameof(WmiSerial),
        nameof(WmiUptime), nameof(WmiCpu), nameof(WmiRam), nameof(WmiDisks), nameof(Findings), nameof(LastSeen)
    ];

    public IReadOnlyDictionary<string, string> ToRecord()
    {
        Dictionary<string, string> record = [];

        foreach (string field in Stored)
        {
            object? value = typeof(HostRow).GetProperty(field)!.GetValue(this);
            record[field] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return record;
    }

    public static HostRow FromRecord(IReadOnlyDictionary<string, string> record)
    {
        HostRow row = new();

        foreach (string field in Stored)
        {
            if (!record.TryGetValue(field, out string? value))
            {
                continue;
            }

            System.Reflection.PropertyInfo property = typeof(HostRow).GetProperty(field)!;
            property.SetValue(row, property.PropertyType == typeof(int)
                ? int.TryParse(value, CultureInfo.InvariantCulture, out int number) ? number : NeverChecked
                : value);
        }

        row.SortKey = Sortable(row.IP);
        row.PortCount = row.OpenPorts.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
        row.AverageMs = double.TryParse(new string([.. row.RoundTrip.TakeWhile(c => char.IsDigit(c) || c == '.')]),
            CultureInfo.InvariantCulture, out double ms) ? ms : double.MaxValue;

        return row;
    }

    public static long Sortable(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress? parsed))
        {
            return long.MaxValue;
        }

        byte[] octets = parsed.GetAddressBytes();

        return octets.Length != 4
            ? long.MaxValue - 1
            : ((long)octets[0] << 24) | ((long)octets[1] << 16) | ((long)octets[2] << 8) | octets[3];
    }

    private static string? First(params string[] candidates) => candidates.FirstOrDefault(value => value.Length > 0);
}
