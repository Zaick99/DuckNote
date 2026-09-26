namespace DuckNote.Scan;

public sealed class ScanOptions
{
    public int Concurrency { get; init; } = 64;

    public int PingCount { get; init; } = 2;
    public TimeSpan PingTimeout { get; init; } = TimeSpan.FromMilliseconds(800);
    public TimeSpan PortTimeout { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan BannerTimeout { get; init; } = TimeSpan.FromMilliseconds(900);

    public IReadOnlyList<int> Ports { get; init; } = DefaultPorts;

    public bool ScanDeadHosts { get; init; }
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
