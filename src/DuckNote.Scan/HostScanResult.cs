namespace DuckNote.Scan;

public sealed class HostScanResult
{
    public required string Address { get; set; }

    public required string Input { get; init; }

    public HostStatus Status { get; set; } = HostStatus.Unreachable;
    public string StatusText => HostStatusText.Describe(Status);

    public string Hostname { get; set; } = string.Empty;
    public string NetBiosName { get; set; } = string.Empty;
    public string MdnsName { get; set; } = string.Empty;
    public string Workgroup { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;

    public string Mac { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;

    public string RoundTrip { get; set; } = string.Empty;
    public double AverageMs { get; set; }
    public string Loss { get; set; } = string.Empty;
    public string Ttl { get; set; } = string.Empty;
    public string Hops { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;

    public IReadOnlyList<int> OpenPorts { get; set; } = [];
    public string Services { get; set; } = string.Empty;

    public string HttpTitle { get; set; } = string.Empty;
    public string HttpServer { get; set; } = string.Empty;
    public string TlsSubject { get; set; } = string.Empty;
    public string TlsIssuer { get; set; } = string.Empty;
    public string TlsExpiry { get; set; } = string.Empty;
    public string TlsProtocol { get; set; } = string.Empty;

    public string SshBanner { get; set; } = string.Empty;
    public string FtpBanner { get; set; } = string.Empty;
    public string SmtpBanner { get; set; } = string.Empty;
    public string RdpInfo { get; set; } = string.Empty;

    public string SnmpName { get; set; } = string.Empty;
    public string SnmpDescription { get; set; } = string.Empty;
    public string SnmpLocation { get; set; } = string.Empty;
    public string SnmpContact { get; set; } = string.Empty;
    public string SnmpUptime { get; set; } = string.Empty;
    public string SnmpObjectId { get; set; } = string.Empty;

    public string UpnpDevice { get; set; } = string.Empty;
    public string UpnpServer { get; set; } = string.Empty;
    public string UpnpLocation { get; set; } = string.Empty;

    public string Shares { get; set; } = string.Empty;
    public string LoggedUser { get; set; } = string.Empty;

    public string WmiOs { get; set; } = string.Empty;
    public string WmiModel { get; set; } = string.Empty;
    public string WmiSerial { get; set; } = string.Empty;
    public string WmiUptime { get; set; } = string.Empty;
    public string WmiCpu { get; set; } = string.Empty;
    public string WmiRam { get; set; } = string.Empty;
    public string WmiDisks { get; set; } = string.Empty;

    public string Findings { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;

    public bool Deep { get; set; }

    public int ElapsedMs { get; set; }

    public bool IsAlive => Status is HostStatus.Online or HostStatus.IcmpFiltered;
}
