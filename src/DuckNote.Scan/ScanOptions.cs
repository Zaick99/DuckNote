namespace DuckNote.Scan;

public sealed class ScanOptions
{
    public int Concurrency { get; init; } = 256;

    public int Sockets { get; init; } = 900;

    public int PingCount { get; init; } = 2;
    public TimeSpan PingTimeout { get; init; } = TimeSpan.FromMilliseconds(800);
    public TimeSpan PortTimeout { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan BannerTimeout { get; init; } = TimeSpan.FromMilliseconds(900);

    public IReadOnlyList<int> Ports { get; init; } = DefaultPorts;

    public bool ScanDeadHosts { get; init; }

    /// <summary>
    /// Un host che non risponde al ping si prova sulle porte (<see cref="LivenessPorts"/> e
    /// quelle che aveva aperte) prima di dirlo spento: risponde anche chi rifiuta la connessione.
    /// </summary>
    public bool ProbePortsWhenSilent { get; init; } = true;

    /// <summary>Si provano anche le porte che aiutano a riconoscere l'apparato (iPhone, AirPlay, Cast, adb...).</summary>
    public bool IdentifyDevices { get; init; } = true;

    /// <summary>Le porte della prova di vita: le piu' comuni su PC, telefoni, stampanti, apparati di rete.</summary>
    public static readonly IReadOnlyList<int> LivenessPorts = [80, 443, 22, 445, 139, 3389, 62078, 8080, 53, 7000, 9100, 554, 5000, 8443];
    public bool ResolveDns { get; init; } = true;
    public bool ProbeNetBios { get; init; } = true;
    public bool ProbeMdns { get; init; } = true;
    public bool ProbeSsdp { get; init; } = true;
    public bool ProbeSnmp { get; init; } = true;
    public bool ProbeBanners { get; init; } = true;
    public bool ProbeShares { get; init; } = true;
    public bool ProbeWmi { get; init; } = true;

    public string SnmpCommunity { get; init; } = "public";

    public string DnsServer { get; init; } = string.Empty;

    public static readonly IReadOnlyList<int> DefaultPorts =
    [
        21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 515, 631, 993, 995,
        1433, 1723, 3000, 3306, 3389, 5000, 5432, 5900, 5985, 5986, 8000, 8006,
        8080, 8443, 8888, 9090, 9100, 10000
    ];
}
