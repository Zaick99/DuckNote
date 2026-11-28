using System.Globalization;
using System.Text.RegularExpressions;
using DuckNote.Scan.Probes;

namespace DuckNote.Scan;

/// <summary>Che cosa si sa per riconoscere un host: tutto quello che la scansione ha raccolto.</summary>
public sealed record DeviceSignals
{
    public int Ttl { get; init; }
    public string Address { get; init; } = string.Empty;
    public string Mac { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public IReadOnlyCollection<int> OpenPorts { get; init; } = [];
    public string Hostname { get; init; } = string.Empty;
    public string MdnsName { get; init; } = string.Empty;
    public string NetBiosName { get; init; } = string.Empty;
    public MdnsServices Mdns { get; init; } = MdnsServices.Empty;
    public string SshBanner { get; init; } = string.Empty;
    public string FtpBanner { get; init; } = string.Empty;
    public string HttpServer { get; init; } = string.Empty;
    public string HttpTitle { get; init; } = string.Empty;
    public string TlsSubject { get; init; } = string.Empty;
    public string SnmpDescription { get; init; } = string.Empty;
    public string UpnpServer { get; init; } = string.Empty;
    public string UpnpDevice { get; init; } = string.Empty;
    public string WmiOs { get; init; } = string.Empty;
    public string WmiModel { get; init; } = string.Empty;

    public static DeviceSignals From(HostScanResult result, IReadOnlyCollection<int> open, MdnsServices mdns) => new()
    {
        Ttl = int.TryParse(result.Ttl, NumberStyles.None, CultureInfo.InvariantCulture, out int ttl) ? ttl : 0,
        Address = result.Address,
        Mac = result.Mac,
        Vendor = result.Vendor,
        OpenPorts = open,
        Hostname = result.Hostname,
        MdnsName = result.MdnsName,
        NetBiosName = result.NetBiosName,
        Mdns = mdns,
        SshBanner = result.SshBanner,
        FtpBanner = result.FtpBanner,
        HttpServer = result.HttpServer,
        HttpTitle = result.HttpTitle,
        TlsSubject = result.TlsSubject,
        SnmpDescription = result.SnmpDescription,
        UpnpServer = result.UpnpServer,
        UpnpDevice = result.UpnpDevice,
        WmiOs = result.WmiOs,
        WmiModel = result.WmiModel,
    };
}

/// <summary>Il sistema, il tipo di apparato, il modello (se si sa) e gli indizi che l'hanno detto.</summary>
public sealed record DeviceIdentityResult(string OperatingSystem, string DeviceType, string Model, string Evidence);

/// <summary>
/// Riconosce un host mettendo insieme gli indizi, ognuno col suo peso, invece di fidarsi di uno
/// solo. Il TTL dice la famiglia (64: Linux, macOS, iOS, Android; 128: Windows; 255: apparati di
/// rete) e da solo non basta a dire Windows o Linux con certezza; il produttore della scheda di
/// rete, le porte tipiche (62078 c'e' solo sugli iPhone e gli iPad, 135 solo su Windows, 5555 e'
/// adb di Android), i servizi annunciati con mDNS (il modello del Mac, AirPlay, Chromecast), i
/// nomi (iPhone-di-Marta, DESKTOP-4F2K9QX, Galaxy-S23), i banner di SSH, HTTP, SNMP e UPnP e,
/// quando c'e', WMI. Vince il sistema con piu' punti; con pochi indizi si dice solo la famiglia.
/// </summary>
public static partial class DeviceIdentity
{
    private enum Os { Windows, MacOs, Ios, Android, Linux, Bsd, Network, Embedded }

    // i tipi di apparato (le stesse parole che la mappa riconosce)
    public const string Printer = "Stampante";
    public const string Camera = "Videocamera / NVR";
    public const string Proxmox = "Proxmox VE";
    public const string Hypervisor = "Host virtualizzazione";
    public const string MediaServer = "Media server";
    public const string Nas = "NAS";
    public const string Router = "Router / Gateway";
    public const string Switch = "Switch gestito";
    public const string AccessPoint = "Access point";
    public const string Voip = "VoIP";
    public const string WindowsPc = "PC Windows";
    public const string WindowsServer = "Server Windows";
    public const string Mac = "Mac";
    public const string IPhone = "iPhone";
    public const string IPad = "iPad";
    public const string AppleMobile = "iPhone / iPad";
    public const string AppleTv = "Apple TV";
    public const string HomePod = "HomePod";
    public const string AndroidPhone = "Telefono / tablet Android";
    public const string Mobile = "Telefono / tablet";
    public const string TvCast = "TV / Chromecast";
    public const string SmartTv = "Smart TV";
    public const string Console = "Console";
    public const string Speaker = "Altoparlante";
    public const string Amazon = "Amazon Echo / Fire TV";
    public const string LinuxHost = "Host Linux / Unix";
    public const string RaspberryPi = "Raspberry Pi";
    public const string HomeAssistant = "Home Assistant";
    public const string VirtualMachine = "Macchina virtuale";
    public const string Iot = "Dispositivo IoT";
    public const string WebDevice = "Dispositivo con web UI";
    public const string Generic = "Host generico";

    /// <summary>Le porte che aiutano a riconoscere un apparato: si provano sempre, oltre a quelle scelte.</summary>
    public static readonly IReadOnlyList<int> IdentityPorts = [62078, 7000, 8009, 5555, 548, 88, 135, 8291, 8123];

    private sealed class Tally
    {
        public readonly Dictionary<Os, int> Os = [];
        public readonly Dictionary<string, int> Type = [];
        public readonly List<(int Weight, string Why)> Reasons = [];
        public string Distro = string.Empty;
        public string Model = string.Empty;
        public string Version = string.Empty;

        public void Add(Os os, int points, string? why = null)
        {
            Os[os] = Os.GetValueOrDefault(os) + points;
            Note(points, why);
        }

        public void Kind(string type, int points, string? why = null)
        {
            Type[type] = Type.GetValueOrDefault(type) + points;
            Note(points, why);
        }

        public void Note(int points, string? why)
        {
            if (why is { Length: > 0 } && !Reasons.Exists(reason => reason.Why == why))
            {
                Reasons.Add((points, why));
            }
        }

        public int Score(Os os) => Os.GetValueOrDefault(os);
    }

    public static DeviceIdentityResult Identify(DeviceSignals s)
    {
        Tally t = new();
        HashSet<int> ports = [.. s.OpenPorts];
        string names = $"{s.Hostname} {s.MdnsName} {s.NetBiosName} {s.Mdns.Value("fn")}".Trim();

        Ttl(s, t);
        Hardware(s, t);
        Ports(s, ports, t);
        Names(names, t);
        Mdns(s.Mdns, t);
        Banners(s, t);

        if (s.WmiOs.Length > 0)
        {
            t.Add(Os.Windows, 300, "WMI");
            t.Kind(s.WmiOs.Contains("Server", StringComparison.OrdinalIgnoreCase) ? WindowsServer : WindowsPc, 120);
            t.Model = s.WmiModel;
        }

        Os? os = Winner(t, s.Ttl);
        string type = TypeOf(t, os, s, ports);
        string system = SystemName(os, t, s, type);
        string evidence = string.Join(" · ", t.Reasons.OrderByDescending(reason => reason.Weight).Take(4).Select(reason => reason.Why));
        return new DeviceIdentityResult(system, type, t.Model, evidence);
    }

    // ------------------------------------------------------------------ TTL

    private static void Ttl(DeviceSignals s, Tally t)
    {
        switch (s.Ttl)
        {
            case <= 0:
                return;
            case <= 32:
                t.Add(Os.Embedded, 15, $"TTL {s.Ttl}");
                return;
            case <= 64:
                // la famiglia Unix: Linux, macOS, iOS, Android, BSD partono da 64; Windows mai
                foreach (Os unix in (Os[])[Os.Linux, Os.MacOs, Os.Ios, Os.Android, Os.Bsd])
                {
                    t.Add(unix, 12);
                }
                t.Add(Os.Windows, -80, $"TTL {s.Ttl}");
                return;
            case <= 128:
                t.Add(Os.Windows, 45, $"TTL {s.Ttl}");
                foreach (Os unix in (Os[])[Os.Linux, Os.MacOs, Os.Ios, Os.Android, Os.Bsd])
                {
                    t.Add(unix, -40);
                }
                return;
            default:
                t.Add(Os.Network, 35, $"TTL {s.Ttl}");
                t.Add(Os.Windows, -40);
                return;
        }
    }

    // ------------------------------------------------------------------ produttore della scheda di rete

    private static void Hardware(DeviceSignals s, Tally t)
    {
        string mac = HexOnly().Replace(s.Mac, string.Empty).ToUpperInvariant();
        if (mac.Length >= 6 && byte.TryParse(mac.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte first) && (first & 0x02) != 0)
        {
            if (mac.StartsWith("525400", StringComparison.Ordinal))
            {
                t.Kind(VirtualMachine, 60, "MAC di QEMU/KVM");
            }
            else if (mac.StartsWith("0242", StringComparison.Ordinal))
            {
                t.Kind(VirtualMachine, 40, "MAC di Docker");
                t.Add(Os.Linux, 20);
            }
            else if (s.Ttl is > 32 and <= 64)
            {
                // indirizzo privato (iOS 14+, Android 10+): quasi sempre un telefono o un tablet
                t.Kind(Mobile, 35, "MAC privato");
                t.Add(Os.Ios, 6);
                t.Add(Os.Android, 6);
            }
            else
            {
                t.Note(5, "MAC privato");
            }
            return;
        }

        string vendor = s.Vendor;
        if (vendor.Length == 0 || vendor.StartsWith("MAC locale", StringComparison.Ordinal))
        {
            return;
        }

        string why = $"MAC {Clean(vendor)}";
        if (AppleVendor().IsMatch(vendor))
        {
            t.Add(Os.MacOs, 25, why);
            t.Add(Os.Ios, 25);
        }
        else if (PhoneVendor().IsMatch(vendor))
        {
            t.Add(Os.Android, 35, why);
            t.Kind(AndroidPhone, 20);
        }
        else if (SamsungVendor().IsMatch(vendor))
        {
            // telefoni, ma anche TV ed elettrodomestici
            t.Add(Os.Android, 20, why);
            t.Kind(AndroidPhone, 10);
        }
        else if (GoogleVendor().IsMatch(vendor))
        {
            t.Add(Os.Android, 20, why);
            t.Kind(TvCast, 15);
        }
        else if (AmazonVendor().IsMatch(vendor))
        {
            t.Add(Os.Android, 20, why);
            t.Kind(Amazon, 45);
        }
        else if (RaspberryVendor().IsMatch(vendor))
        {
            t.Add(Os.Linux, 50, why);
            t.Kind(RaspberryPi, 60);
            t.Distro = "Raspberry Pi OS";
        }
        else if (IotVendor().IsMatch(vendor))
        {
            t.Add(Os.Embedded, 45, why);
            t.Kind(Iot, 50);
        }
        else if (ConsoleVendor().IsMatch(vendor))
        {
            t.Add(Os.Embedded, 30, why);
            t.Kind(Console, 70);
        }
        else if (MicrosoftVendor().IsMatch(vendor))
        {
            t.Add(Os.Windows, 25, why);
        }
        else if (PcVendor().IsMatch(vendor))
        {
            t.Kind(Generic, 5, why);
        }
        else if (NasVendor().IsMatch(vendor))
        {
            t.Add(Os.Linux, 30, why);
            t.Kind(Nas, 60);
        }
        else if (PrinterVendor().IsMatch(vendor))
        {
            t.Add(Os.Embedded, 25, why);
            t.Kind(Printer, 45);
        }
        else if (CameraVendor().IsMatch(vendor))
        {
            t.Add(Os.Embedded, 25, why);
            t.Kind(Camera, 50);
        }
        else if (SpeakerVendor().IsMatch(vendor))
        {
            t.Add(Os.Embedded, 20, why);
            t.Kind(Speaker, 55);
        }
        else if (TvVendor().IsMatch(vendor))
        {
            t.Kind(SmartTv, 25, why);
        }
        else if (VmVendor().IsMatch(vendor))
        {
            t.Kind(VirtualMachine, 55, why);
        }
        else if (NetworkVendor().IsMatch(vendor))
        {
            t.Add(Os.Network, 20, why);
            t.Kind(Router, 10);
            if (UbiquitiVendor().IsMatch(vendor))
            {
                t.Kind(AccessPoint, 12);
            }
        }
        else if (VoipVendor().IsMatch(vendor))
        {
            t.Kind(Voip, 50, why);
        }
        else
        {
            t.Note(3, why);
        }
    }

    // ------------------------------------------------------------------ porte

    private static void Ports(DeviceSignals s, HashSet<int> ports, Tally t)
    {
        bool unix = s.Ttl is > 32 and <= 64;
        bool windows = s.Ttl is > 64 and <= 128;

        if (ports.Contains(62078))
        {
            t.Add(Os.Ios, 90, "porta 62078 (iPhone/iPad)");
            t.Kind(AppleMobile, 80);
        }
        if (ports.Contains(5555))
        {
            t.Add(Os.Android, 50, "porta 5555 (adb)");
        }
        if (ports.Contains(8008) || ports.Contains(8009))
        {
            t.Add(Os.Android, 20, "Google Cast");
            t.Kind(TvCast, 60);
        }
        if (ports.Contains(7000))
        {
            t.Add(Os.MacOs, 12, "AirPlay");
            t.Add(Os.Ios, 6);
        }
        if (ports.Contains(3689))
        {
            t.Add(Os.MacOs, 15, "porta 3689 (iTunes)");
        }
        if (ports.Contains(548))
        {
            t.Add(Os.MacOs, 15, "porta 548 (AFP)");
            t.Kind(Nas, 10);
        }
        if (ports.Contains(88) && unix && (ports.Contains(445) || ports.Contains(548) || ports.Contains(22)))
        {
            t.Add(Os.MacOs, 35, "Kerberos di macOS");
            t.Kind(Mac, 25);
        }
        if (ports.Contains(135))
        {
            t.Add(Os.Windows, 60, "porta 135 (RPC di Windows)");
        }
        if (ports.Contains(445) || ports.Contains(139))
        {
            if (windows)
            {
                t.Add(Os.Windows, 25, "condivisioni SMB");
            }
            else if (unix)
            {
                // Samba: Linux, macOS, un NAS
                t.Add(Os.Linux, 8);
                t.Add(Os.MacOs, 8);
            }
        }
        if (ports.Contains(3389))
        {
            if (unix)
            {
                t.Add(Os.Linux, 12, "RDP (xrdp)");
            }
            else
            {
                t.Add(Os.Windows, 50, "RDP");
            }
        }
        if (ports.Contains(5985) || ports.Contains(5986))
        {
            t.Add(Os.Windows, 60, "WinRM");
        }
        if (ports.Contains(22))
        {
            t.Add(Os.Linux, 10, "SSH");
            t.Add(Os.MacOs, 4);
            t.Add(Os.Bsd, 4);
            t.Add(Os.Network, 3);
        }
        if (ports.Contains(9100) || ports.Contains(515) || ports.Contains(631))
        {
            t.Kind(Printer, 75, "porta di stampa");
        }
        if (ports.Contains(554))
        {
            t.Kind(Camera, 55, "RTSP");
        }
        if (ports.Contains(8006))
        {
            t.Add(Os.Linux, 70, "Proxmox (8006)");
            t.Kind(Proxmox, 90);
            t.Distro = "Proxmox VE";
        }
        if (ports.Contains(902))
        {
            t.Kind(Hypervisor, 60, "VMware (902)");
        }
        if (ports.Contains(32400))
        {
            t.Kind(MediaServer, 60, "Plex");
        }
        if (ports.Contains(2049))
        {
            t.Kind(Nas, 50, "NFS");
            t.Add(Os.Linux, 15);
        }
        if (ports.Contains(8291))
        {
            t.Add(Os.Network, 80, "Winbox (MikroTik)");
            t.Kind(Router, 60);
            t.Distro = "RouterOS";
        }
        if (ports.Contains(8123))
        {
            t.Add(Os.Linux, 40, "Home Assistant (8123)");
            t.Kind(HomeAssistant, 70);
        }
        if (ports.Contains(5060))
        {
            t.Kind(Voip, 50, "SIP");
        }
        if (ports.Contains(53) && (ports.Contains(80) || ports.Contains(443)))
        {
            t.Kind(Router, 25, "DNS e pagina web");
        }
    }

    // ------------------------------------------------------------------ nomi

    private static void Names(string names, Tally t)
    {
        if (names.Length == 0)
        {
            return;
        }

        if (IPhoneName().IsMatch(names))
        {
            t.Add(Os.Ios, 70, "nome iPhone");
            t.Kind(IPhone, 80);
        }
        else if (IPadName().IsMatch(names))
        {
            t.Add(Os.Ios, 70, "nome iPad");
            t.Kind(IPad, 80);
        }
        else if (MacName().IsMatch(names))
        {
            t.Add(Os.MacOs, 60, "nome Mac");
            t.Kind(Mac, 70);
        }
        else if (AppleTvName().IsMatch(names))
        {
            t.Add(Os.Ios, 50, "nome Apple TV");
            t.Kind(AppleTv, 80);
        }
        else if (HomePodName().IsMatch(names))
        {
            t.Add(Os.Ios, 40, "nome HomePod");
            t.Kind(HomePod, 80);
        }

        if (AndroidName().IsMatch(names))
        {
            t.Add(Os.Android, 45, "nome Android");
            t.Kind(AndroidPhone, 45);
        }
        if (WindowsName().IsMatch(names))
        {
            t.Add(Os.Windows, 60, "nome di Windows");
            t.Kind(WindowsPc, 40);
        }
        if (WindowsServerName().IsMatch(names))
        {
            t.Add(Os.Windows, 50, "nome di Windows Server");
            t.Kind(WindowsServer, 40);
        }
        if (RaspberryName().IsMatch(names))
        {
            t.Add(Os.Linux, 45, "nome Raspberry Pi");
            t.Kind(RaspberryPi, 50);
            t.Distro = t.Distro.Length > 0 ? t.Distro : "Raspberry Pi OS";
        }
        Match distro = DistroName().Match(names);
        if (distro.Success)
        {
            t.Add(Os.Linux, 35, "nome Linux");
            t.Kind(LinuxHost, 20);
        }
        if (CastName().IsMatch(names))
        {
            t.Add(Os.Android, 20, "nome Chromecast/Google");
            t.Kind(TvCast, 50);
        }
        if (ConsoleName().IsMatch(names))
        {
            t.Kind(Console, 60, "nome di una console");
        }
        if (PrinterName().IsMatch(names))
        {
            t.Kind(Printer, 45, "nome di stampante");
        }
        if (NasName().IsMatch(names))
        {
            t.Kind(Nas, 40, "nome di NAS");
        }
        if (AmazonName().IsMatch(names))
        {
            t.Kind(Amazon, 50, "nome Amazon");
            t.Add(Os.Android, 15);
        }
    }

    // ------------------------------------------------------------------ mDNS (DNS-SD)

    private static void Mdns(MdnsServices m, Tally t)
    {
        if (m.IsEmpty)
        {
            return;
        }

        string model = m.Value("model");
        string am = m.Value("am");
        string apple = model.Length > 0 ? model : am;
        if (apple.Length > 0 && AppleModel(apple) is { } friendly)
        {
            t.Model = $"{friendly.Name} ({apple})";
            t.Add(friendly.Os, 120, $"mDNS: {apple}");
            t.Kind(friendly.Type, 120);
            if (friendly.Os == Os.MacOs)
            {
                t.Version = MacOsName(m.Value("osxvers"));
            }
        }
        else if (model.Length > 0 && m.Has("device-info"))
        {
            // Samba (Time Machine) annuncia "model=MacSamba" o "RackMac": e' un Linux o un NAS
            if (SambaModel().IsMatch(model))
            {
                t.Add(Os.Linux, 30, "Samba (mDNS)");
            }
        }

        if (m.Has("apple-mobdev2"))
        {
            t.Add(Os.Ios, 80, "mDNS: sincronizzazione iPhone");
            t.Kind(AppleMobile, 70);
        }
        if (m.Has("companion-link") || m.Has("rdlink"))
        {
            t.Add(Os.Ios, 25, "mDNS: servizi Apple");
            t.Add(Os.MacOs, 25);
        }
        if (m.Has("sleep-proxy"))
        {
            t.Add(Os.Ios, 20, "mDNS: Apple TV / HomePod");
            t.Kind(AppleTv, 20);
        }
        if (m.Has("airplay") && apple.Length == 0)
        {
            t.Add(Os.MacOs, 10, "mDNS: AirPlay");
            t.Add(Os.Ios, 10);
        }
        if (m.Has("googlecast"))
        {
            string md = m.Value("md");
            t.Add(Os.Android, 40, md.Length > 0 ? $"mDNS: {md}" : "mDNS: Google Cast");
            t.Kind(TvCast, 90);
            if (md.Length > 0)
            {
                t.Model = md;
            }
        }
        if (m.Has("androidtvremote2"))
        {
            t.Add(Os.Android, 60, "mDNS: Android TV");
            t.Kind(TvCast, 70);
        }
        if (m.Has("ipp") || m.Has("printer") || m.Has("pdl-datastream"))
        {
            string printer = m.Value("ty").Length > 0 ? m.Value("ty") : m.Value("product").Trim('(', ')');
            t.Kind(Printer, 100, printer.Length > 0 ? $"mDNS: {printer}" : "mDNS: stampante");
            t.Add(Os.Embedded, 30);
            if (printer.Length > 0)
            {
                t.Model = printer;
            }
        }
        if (m.Has("hap"))
        {
            t.Kind(Iot, 60, "mDNS: HomeKit");
            t.Add(Os.Embedded, 25);
        }
        if (m.Has("workstation"))
        {
            t.Add(Os.Linux, 45, "mDNS: Avahi (Linux)");
            t.Kind(LinuxHost, 25);
        }
        if (m.Has("spotify-connect") && !m.Has("googlecast"))
        {
            t.Kind(Speaker, 30, "mDNS: Spotify Connect");
        }
    }

    private sealed record AppleKind(string Name, Os Os, string Type);

    /// <summary>Il nome di un identificativo di modello Apple: "MacBookPro18,3" e' un MacBook Pro.</summary>
    private static AppleKind? AppleModel(string model)
    {
        string m = model.Trim();
        return m switch
        {
            _ when m.StartsWith("MacBookPro", StringComparison.OrdinalIgnoreCase) => new("MacBook Pro", Os.MacOs, Mac),
            _ when m.StartsWith("MacBookAir", StringComparison.OrdinalIgnoreCase) => new("MacBook Air", Os.MacOs, Mac),
            _ when m.StartsWith("MacBook", StringComparison.OrdinalIgnoreCase) => new("MacBook", Os.MacOs, Mac),
            _ when m.StartsWith("iMacPro", StringComparison.OrdinalIgnoreCase) => new("iMac Pro", Os.MacOs, Mac),
            _ when m.StartsWith("iMac", StringComparison.OrdinalIgnoreCase) => new("iMac", Os.MacOs, Mac),
            _ when m.StartsWith("Macmini", StringComparison.OrdinalIgnoreCase) => new("Mac mini", Os.MacOs, Mac),
            _ when m.StartsWith("MacPro", StringComparison.OrdinalIgnoreCase) => new("Mac Pro", Os.MacOs, Mac),
            _ when MacGeneric().IsMatch(m) => new("Mac", Os.MacOs, Mac),
            _ when m.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase) => new("iPhone", Os.Ios, IPhone),
            _ when m.StartsWith("iPad", StringComparison.OrdinalIgnoreCase) => new("iPad", Os.Ios, IPad),
            _ when m.StartsWith("AppleTV", StringComparison.OrdinalIgnoreCase) => new("Apple TV", Os.Ios, AppleTv),
            _ when m.StartsWith("AudioAccessory", StringComparison.OrdinalIgnoreCase) => new("HomePod", Os.Ios, HomePod),
            _ => null
        };
    }

    /// <summary>La versione di macOS dal numero di Darwin che il Mac annuncia (osxvers).</summary>
    private static string MacOsName(string darwin) => darwin switch
    {
        "19" => "macOS 10.15 Catalina",
        "20" => "macOS 11 Big Sur",
        "21" => "macOS 12 Monterey",
        "22" => "macOS 13 Ventura",
        "23" => "macOS 14 Sonoma",
        "24" => "macOS 15 Sequoia",
        "25" => "macOS 26 Tahoe",
        _ => string.Empty
    };

    // ------------------------------------------------------------------ banner, SNMP, UPnP, pagine web

    private static void Banners(DeviceSignals s, Tally t)
    {
        string ssh = s.SshBanner;
        if (ssh.Length > 0)
        {
            if (ssh.Contains("for_Windows", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Windows, 80, "SSH di Windows");
            }
            else if (RouterOsSsh().IsMatch(ssh))
            {
                t.Add(Os.Network, 90, "SSH di RouterOS");
                t.Kind(Router, 60);
                t.Distro = "RouterOS";
            }
            else if (ssh.Contains("Cisco", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Network, 90, "SSH di Cisco");
                t.Kind(Switch, 30);
                t.Distro = "Cisco IOS";
            }
            else if (ssh.Contains("dropbear", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Linux, 30, "SSH dropbear (Linux embedded)");
                t.Add(Os.Embedded, 20);
            }
            else if (BsdText().Match(ssh) is { Success: true } bsd)
            {
                t.Add(Os.Bsd, 70, $"SSH di {bsd.Value}");
                t.Distro = Capital(bsd.Value);
            }
            else if (DistroText().Match(ssh) is { Success: true } distro)
            {
                t.Add(Os.Linux, 70, $"SSH di {Capital(distro.Value)}");
                t.Distro = Capital(distro.Value) == "Raspbian" ? "Raspberry Pi OS" : Capital(distro.Value);
            }
            else if (ssh.Contains("OpenSSH", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Linux, 10);
                t.Add(Os.MacOs, 10);
                t.Add(Os.Bsd, 5);
            }
        }

        string snmp = s.SnmpDescription;
        if (snmp.Length > 0)
        {
            if (snmp.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Windows, 90, "SNMP");
            }
            else if (snmp.Contains("Darwin", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.MacOs, 80, "SNMP");
            }
            else if (snmp.Contains("RouterOS", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Network, 90, "SNMP");
                t.Kind(Router, 50);
                t.Distro = "RouterOS";
            }
            else if (CiscoText().IsMatch(snmp))
            {
                t.Add(Os.Network, 90, "SNMP");
                t.Kind(Switch, 30);
                t.Distro = snmp.Contains("NX-OS", StringComparison.OrdinalIgnoreCase) ? "Cisco NX-OS" : "Cisco IOS";
            }
            else if (snmp.Contains("JUNOS", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Network, 90, "SNMP");
                t.Distro = "Junos";
            }
            else if (BsdText().Match(snmp) is { Success: true } bsd)
            {
                t.Add(Os.Bsd, 80, "SNMP");
                t.Distro = Capital(bsd.Value);
            }
            else if (snmp.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Linux, 70, "SNMP");
            }
        }

        string upnp = $"{s.UpnpServer} {s.UpnpDevice}";
        if (upnp.Trim().Length > 0)
        {
            if (upnp.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Windows, 60, "UPnP di Windows");
            }
            else if (upnp.Contains("Android", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Android, 60, "UPnP di Android");
            }
            else if (upnp.Contains("Darwin", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.MacOs, 60, "UPnP di macOS");
            }
            else if (upnp.Contains("Tizen", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Linux, 40, "UPnP Tizen");
                t.Kind(SmartTv, 80);
                t.Distro = "Tizen";
            }
            else if (WebOsText().IsMatch(upnp))
            {
                t.Add(Os.Linux, 40, "UPnP webOS");
                t.Kind(SmartTv, 80);
                t.Distro = "webOS";
            }
            else if (upnp.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.Linux, 25, "UPnP Linux");
            }

            if (TvText().IsMatch(upnp))
            {
                t.Kind(SmartTv, 50, "UPnP: TV");
            }
            if (upnp.Contains("Sonos", StringComparison.OrdinalIgnoreCase))
            {
                t.Kind(Speaker, 70, "UPnP: Sonos");
            }
        }

        string web = $"{s.HttpServer} {s.HttpTitle} {s.TlsSubject}";
        if (web.Trim().Length > 0)
        {
            if (IisText().IsMatch(web))
            {
                t.Add(Os.Windows, 40, "web di Windows (IIS)");
            }
            if (web.Contains("AirTunes", StringComparison.OrdinalIgnoreCase))
            {
                t.Add(Os.MacOs, 25, "AirPlay");
                t.Add(Os.Ios, 15);
            }

            foreach ((Regex pattern, string type, Os os, string system, int points) in WebProducts)
            {
                if (pattern.IsMatch(web))
                {
                    t.Kind(type, points, $"pagina di {system}");
                    t.Add(os, points / 2);
                    if (t.Distro.Length == 0)
                    {
                        t.Distro = system;
                    }
                    break;
                }
            }
        }

        // i vecchi indizi generici dei nomi dei produttori (banner, SNMP, UPnP)
        string haystack = $"{s.Vendor} {snmp} {upnp} {web}".ToLowerInvariant();
        if (CameraText().IsMatch(haystack))
        {
            t.Kind(Camera, 45);
        }
        if (NasText().IsMatch(haystack))
        {
            t.Kind(Nas, 40);
        }
        if (RouterText().IsMatch(haystack))
        {
            t.Kind(Router, 40);
        }
        if (SwitchText().IsMatch(haystack))
        {
            t.Kind(Switch, 40);
        }
        if (VoipText().IsMatch(haystack))
        {
            t.Kind(Voip, 45);
        }
        if (IotText().IsMatch(haystack))
        {
            t.Kind(Iot, 40);
        }
        if (MediaText().IsMatch(haystack))
        {
            t.Kind(MediaServer, 40);
        }
        if (HypervisorText().IsMatch(haystack))
        {
            t.Kind(Hypervisor, 45);
        }
    }

    private static readonly (Regex Pattern, string Type, Os Os, string System, int Points)[] WebProducts =
    [
        (Make("synology|diskstation"), Nas, Os.Linux, "Synology DSM", 80),
        (Make("qnap|qts"), Nas, Os.Linux, "QNAP QTS", 80),
        (Make("truenas"), Nas, Os.Bsd, "TrueNAS", 80),
        (Make("unraid"), Nas, Os.Linux, "Unraid", 80),
        (Make("proxmox"), Proxmox, Os.Linux, "Proxmox VE", 90),
        (Make("pfsense"), Router, Os.Bsd, "pfSense", 90),
        (Make("opnsense"), Router, Os.Bsd, "OPNsense", 90),
        (Make("openwrt|luci"), Router, Os.Linux, "OpenWrt", 80),
        (Make("routeros|mikrotik|webfig"), Router, Os.Network, "RouterOS", 90),
        (Make("unifi"), AccessPoint, Os.Linux, "UniFi", 60),
        (Make("fritz!?box"), Router, Os.Linux, "FRITZ!OS", 90),
        (Make("home assistant"), HomeAssistant, Os.Linux, "Home Assistant", 90),
        (Make("pi-hole"), LinuxHost, Os.Linux, "Pi-hole", 60),
        (Make("esxi|vmware"), Hypervisor, Os.Embedded, "VMware ESXi", 80),
        (Make("idrac"), Generic, Os.Embedded, "iDRAC", 40),
        (Make("integrated lights-out|(?<![a-z])ilo(?![a-z])"), Generic, Os.Embedded, "iLO", 40),
        (Make("plex"), MediaServer, Os.Linux, "Plex", 60),
    ];

    // ------------------------------------------------------------------ la decisione

    private static Os? Winner(Tally t, int ttl)
    {
        if (t.Os.Count == 0)
        {
            return null;
        }

        (Os os, int score) = t.Os.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value)).First();
        // con il solo TTL (12 punti a ogni Unix, 35 agli apparati di rete) non si sceglie
        int needed = os is Os.Windows or Os.Network ? 40 : 30;
        if (score < needed)
        {
            return null;
        }

        // Windows parte da 128: con un TTL da 64 serve un indizio fortissimo (WMI)
        if (os == Os.Windows && ttl is > 0 and <= 64 && score < 150)
        {
            return null;
        }

        return os;
    }

    private static string TypeOf(Tally t, Os? os, DeviceSignals s, HashSet<int> ports)
    {
        // il tipo che il sistema suggerisce, se nient'altro lo dice meglio
        switch (os)
        {
            case Os.Windows:
                t.Kind(t.Type.ContainsKey(WindowsServer) ? WindowsServer : WindowsPc, 30);
                break;
            case Os.MacOs:
                t.Kind(Mac, 30);
                break;
            case Os.Ios when !t.Type.ContainsKey(AppleTv) && !t.Type.ContainsKey(HomePod):
                t.Kind(AppleMobile, 30);
                break;
            case Os.Android:
                t.Kind(AndroidPhone, 25);
                break;
            case Os.Linux:
                t.Kind(LinuxHost, 25);
                break;
            case Os.Bsd:
                t.Kind(LinuxHost, 20);
                break;
            case Os.Network:
                t.Kind(Router, 15);
                break;
        }

        // l'iPhone e l'iPad battono "iPhone / iPad"; un Mac non e' un telefono
        if (t.Type.ContainsKey(IPhone) || t.Type.ContainsKey(IPad))
        {
            t.Type.Remove(AppleMobile);
        }
        if (os == Os.MacOs)
        {
            t.Type.Remove(AppleMobile);
            t.Type.Remove(Mobile);
        }
        if (os is Os.Ios or Os.Android)
        {
            t.Type.Remove(Mobile);
        }

        if (t.Type.Count > 0)
        {
            (string type, int score) = t.Type.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value)).First();
            if (score >= 20)
            {
                return type;
            }
        }

        if (ports.Contains(80) || ports.Contains(443) || ports.Contains(8080))
        {
            return WebDevice;
        }

        return ports.Count > 0 ? Generic : string.Empty;
    }

    private static string SystemName(Os? os, Tally t, DeviceSignals s, string type)
    {
        if (s.WmiOs.Length > 0)
        {
            return s.WmiOs;
        }

        switch (os)
        {
            case Os.Windows:
                return type == WindowsServer ? "Windows Server" : "Windows";
            case Os.MacOs:
                return t.Version.Length > 0 ? t.Version : "macOS";
            case Os.Ios:
                return type switch
                {
                    IPad => "iPadOS",
                    AppleTv => "tvOS",
                    HomePod => "audioOS",
                    IPhone => "iOS",
                    _ => "iOS / iPadOS"
                };
            case Os.Android:
                return type switch
                {
                    TvCast => "Android TV / Google Cast",
                    Amazon => "Fire OS",
                    _ => "Android"
                };
            case Os.Linux:
                return t.Distro.Length > 0 ? t.Distro : "Linux";
            case Os.Bsd:
                return t.Distro.Length > 0 ? t.Distro : "BSD";
            case Os.Network:
                return t.Distro.Length > 0 ? t.Distro : "Sistema di rete";
            case Os.Embedded:
                return t.Distro.Length > 0 ? t.Distro : "Firmware embedded";
        }

        // pochi indizi: la famiglia dal TTL, piu' precisa se si sa almeno che e' un telefono
        if (s.Ttl is > 32 and <= 64 && type == Mobile)
        {
            return "iOS o Android";
        }

        if (t.Distro.Length > 0)
        {
            return t.Distro;
        }

        return TtlFingerprint.GuessOperatingSystem(s.Ttl);
    }

    private static string Clean(string vendor) =>
        vendor.EndsWith(" (da banner)", StringComparison.Ordinal) ? vendor[..^12] : vendor;

    private static string Capital(string word) =>
        word.Length == 0 ? word : word.ToLowerInvariant() switch
        {
            "freebsd" => "FreeBSD",
            "openbsd" => "OpenBSD",
            "netbsd" => "NetBSD",
            "centos" => "CentOS",
            "opensuse" => "openSUSE",
            "almalinux" => "AlmaLinux",
            "rocky" => "Rocky Linux",
            _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
        };

    private static Regex Make(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex("[^0-9A-Fa-f]")]
    private static partial Regex HexOnly();

    [GeneratedRegex(@"\bapple\b", RegexOptions.IgnoreCase)]
    private static partial Regex AppleVendor();

    [GeneratedRegex(@"xiaomi|oppo|\bvivo\b|oneplus|realme|motorola|huawei \(dispositivi\)|huawei device|\bhonor\b|nothing|fairphone|hmd global|\boneplus\b", RegexOptions.IgnoreCase)]
    private static partial Regex PhoneVendor();

    [GeneratedRegex(@"samsung", RegexOptions.IgnoreCase)]
    private static partial Regex SamsungVendor();

    [GeneratedRegex(@"\bgoogle\b", RegexOptions.IgnoreCase)]
    private static partial Regex GoogleVendor();

    [GeneratedRegex(@"\bamazon\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmazonVendor();

    [GeneratedRegex(@"raspberry", RegexOptions.IgnoreCase)]
    private static partial Regex RaspberryVendor();

    [GeneratedRegex(@"espressif|tuya|shelly|allterco|silicon lab|texas instruments|sonoff|itead|philips hue|signify|\bikea\b|netatmo|withings|ring\b|nest", RegexOptions.IgnoreCase)]
    private static partial Regex IotVendor();

    [GeneratedRegex(@"nintendo|playstation|sony interactive|\bvalve\b|xbox", RegexOptions.IgnoreCase)]
    private static partial Regex ConsoleVendor();

    [GeneratedRegex(@"\bmicrosoft\b", RegexOptions.IgnoreCase)]
    private static partial Regex MicrosoftVendor();

    [GeneratedRegex(@"\bintel\b|realtek|lite-?on|azurewave|foxconn|hon hai|qualcomm|atheros|broadcom|mediatek|murata|\bdell\b|\bhp\b(?! \(stampante\))|lenovo|\basus|\bacer\b|\bmsi\b|gigabyte", RegexOptions.IgnoreCase)]
    private static partial Regex PcVendor();

    [GeneratedRegex(@"synology|qnap|western digital|seagate|asustor|terramaster|ugreen", RegexOptions.IgnoreCase)]
    private static partial Regex NasVendor();

    [GeneratedRegex(@"brother|epson|canon|kyocera|lexmark|ricoh|xerox|hp \(stampante\)|konica|sharp|oki\b", RegexOptions.IgnoreCase)]
    private static partial Regex PrinterVendor();

    [GeneratedRegex(@"hikvision|dahua|\baxis\b|arlo|reolink|ezviz|uniview", RegexOptions.IgnoreCase)]
    private static partial Regex CameraVendor();

    [GeneratedRegex(@"sonos|\bbose\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpeakerVendor();

    [GeneratedRegex(@"\blg\b|hisense|\btcl\b|vizio|panasonic|philips(?! hue)|\bsony\b(?! playstation)", RegexOptions.IgnoreCase)]
    private static partial Regex TvVendor();

    [GeneratedRegex(@"vmware|virtualbox|parallels|xensource|qemu", RegexOptions.IgnoreCase)]
    private static partial Regex VmVendor();

    [GeneratedRegex(@"cisco|ubiquiti|mikrotik|routerboard|tp-link|netgear|\bavm\b|aruba|juniper|fortinet|zyxel|d-link|netgate|sophos|palo alto|ruckus|commscope|sagemcom|technicolor|arris|\bzte\b|^huawei$|eero|meraki|draytek|linksys|belkin|tenda|\basus\b(?=.*router)", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkVendor();

    [GeneratedRegex(@"ubiquiti", RegexOptions.IgnoreCase)]
    private static partial Regex UbiquitiVendor();

    [GeneratedRegex(@"grandstream|yealink|polycom|\bpoly\b|snom|gigaset|fanvil", RegexOptions.IgnoreCase)]
    private static partial Regex VoipVendor();

    [GeneratedRegex(@"(?i)\biphone\b|iphone[-_ ]|iphone$")]
    private static partial Regex IPhoneName();

    [GeneratedRegex(@"(?i)\bipad\b|ipad[-_ ]")]
    private static partial Regex IPadName();

    [GeneratedRegex(@"(?i)macbook|\bimac\b|imac[-_]|mac-?mini|mac-?pro|mac-?studio|\bmac\b|-mac\b")]
    private static partial Regex MacName();

    [GeneratedRegex(@"(?i)apple-?tv|appletv")]
    private static partial Regex AppleTvName();

    [GeneratedRegex(@"(?i)homepod")]
    private static partial Regex HomePodName();

    [GeneratedRegex(@"(?i)\bandroid|galaxy|\bsm-[a-z]\d|pixel[-_ ]?\d|\bpixel\b|redmi|xiaomi|\bpoco\b|oneplus|\boppo\b|\bvivo\b|realme|huawei|\bhonor\b|\bmoto[-_ ]?[a-z0-9]|nokia")]
    private static partial Regex AndroidName();

    [GeneratedRegex(@"(?i)\b(desktop|laptop)-[a-z0-9]{7}\b")]
    private static partial Regex WindowsName();

    [GeneratedRegex(@"(?i)\bwin-[a-z0-9]{8,}\b")]
    private static partial Regex WindowsServerName();

    [GeneratedRegex(@"(?i)raspberry|\braspi\b|\brpi\d?\b")]
    private static partial Regex RaspberryName();

    [GeneratedRegex(@"(?i)\b(ubuntu|debian|fedora|archlinux|manjaro|linuxmint|centos|rocky|alma|proxmox|pve\d*)\b")]
    private static partial Regex DistroName();

    [GeneratedRegex(@"(?i)chromecast|google-?tv|google-?home|nest-?(hub|mini|audio)|android-?tv|bravia")]
    private static partial Regex CastName();

    [GeneratedRegex(@"(?i)playstation|\bps[345]\b|xbox|nintendo")]
    private static partial Regex ConsoleName();

    [GeneratedRegex(@"(?i)printer|stampante|laserjet|officejet|deskjet|\benvy\b|\bbrn[0-9a-f]{12}\b|\bepson|\bcanon|\bkyocera|\bnpi[0-9a-f]{6}")]
    private static partial Regex PrinterName();

    [GeneratedRegex(@"(?i)\bnas\b|diskstation|\bds\d{3,4}\b|truenas|unraid|qnap")]
    private static partial Regex NasName();

    [GeneratedRegex(@"(?i)\becho-?(dot|show)?\b|fire-?tv|amazon-[0-9a-f]+|kindle")]
    private static partial Regex AmazonName();

    [GeneratedRegex(@"^(Mac\d+,\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MacGeneric();

    [GeneratedRegex(@"(?i)macsamba|rackmac|xserve")]
    private static partial Regex SambaModel();

    [GeneratedRegex(@"(?i)rosssh|routeros")]
    private static partial Regex RouterOsSsh();

    [GeneratedRegex(@"(?i)freebsd|openbsd|netbsd")]
    private static partial Regex BsdText();

    [GeneratedRegex(@"(?i)ubuntu|debian|raspbian|fedora|centos|almalinux|rocky|opensuse|alpine|arch")]
    private static partial Regex DistroText();

    [GeneratedRegex(@"(?i)cisco ios|ios-xe|nx-os|cisco adaptive")]
    private static partial Regex CiscoText();

    [GeneratedRegex(@"(?i)web0s|webos")]
    private static partial Regex WebOsText();

    [GeneratedRegex(@"(?i)\btv\b|television|bravia|mediarenderer")]
    private static partial Regex TvText();

    [GeneratedRegex(@"(?i)microsoft-iis|microsoft-httpapi")]
    private static partial Regex IisText();

    [GeneratedRegex("hikvision|dahua|axis|camera|ipcam|nvr")]
    private static partial Regex CameraText();

    [GeneratedRegex("synology|qnap|truenas|readynas|diskstation")]
    private static partial Regex NasText();

    [GeneratedRegex("router|gateway|mikrotik|fritz|openwrt|dd-wrt|pfsense|opnsense|edgeos|ios software")]
    private static partial Regex RouterText();

    [GeneratedRegex(@"switch|catalyst|procurve|ex\d{4}")]
    private static partial Regex SwitchText();

    [GeneratedRegex("grandstream|yealink|polycom|snom|asterisk")]
    private static partial Regex VoipText();

    [GeneratedRegex("espressif|shelly|tasmota|sonoff|tuya|philips hue|hue bridge")]
    private static partial Regex IotText();

    [GeneratedRegex("plex|jellyfin|emby")]
    private static partial Regex MediaText();

    [GeneratedRegex("esxi|vmware")]
    private static partial Regex HypervisorText();
}
