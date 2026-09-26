using DuckNote.Scan;

namespace DuckNote.App.Models;

public sealed class ScanRow : Observable
{
    public const int NeverChecked = 4;

    private string _ip = string.Empty;
    private long _sortKey;
    private string _status = string.Empty;
    private int _statusRank;
    private string _hostname = string.Empty;
    private string _netBiosName = string.Empty;
    private string _workgroup = string.Empty;
    private string _mac = string.Empty;
    private string _vendor = string.Empty;
    private string _rttMs = string.Empty;
    private double _rttAvg;
    private string _loss = string.Empty;
    private string _ttl = string.Empty;
    private string _osGuess = string.Empty;
    private string _deviceType = string.Empty;
    private string _openPorts = string.Empty;
    private int _portCount;
    private string _services = string.Empty;
    private string _httpTitle = string.Empty;
    private string _httpServer = string.Empty;
    private string _tlsSubject = string.Empty;
    private string _tlsIssuer = string.Empty;
    private string _tlsExpiry = string.Empty;
    private string _sshBanner = string.Empty;
    private string _ftpBanner = string.Empty;
    private string _smtpBanner = string.Empty;
    private string _rdpInfo = string.Empty;
    private string _snmpName = string.Empty;
    private string _snmpDescr = string.Empty;
    private string _snmpLocation = string.Empty;
    private string _snmpContact = string.Empty;
    private string _snmpUptime = string.Empty;
    private string _mdnsName = string.Empty;
    private string _upnpDevice = string.Empty;
    private string _upnpServer = string.Empty;
    private string _shares = string.Empty;
    private string _loggedUser = string.Empty;
    private string _wmiOs = string.Empty;
    private string _wmiModel = string.Empty;
    private string _wmiSerial = string.Empty;
    private string _wmiUptime = string.Empty;
    private string _wmiCpu = string.Empty;
    private string _wmiRam = string.Empty;
    private string _wmiDisks = string.Empty;
    private string _domain = string.Empty;
    private string _comment = string.Empty;
    private string _lastSeen = string.Empty;
    private int _scanMs;
    private string _notes = string.Empty;
    private string _dotColor = string.Empty;
    private string _noteKey = string.Empty;

    public string IP { get => _ip; set => Set(ref _ip, value); }
    public long SortKey { get => _sortKey; set => Set(ref _sortKey, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public int StatusRank { get => _statusRank; set => Set(ref _statusRank, value); }
    public string Hostname { get => _hostname; set => Set(ref _hostname, value); }
    public string NetBiosName { get => _netBiosName; set => Set(ref _netBiosName, value); }
    public string Workgroup { get => _workgroup; set => Set(ref _workgroup, value); }
    public string Mac { get => _mac; set => Set(ref _mac, value); }
    public string Vendor { get => _vendor; set => Set(ref _vendor, value); }
    public string RttMs { get => _rttMs; set => Set(ref _rttMs, value); }
    public double RttAvg { get => _rttAvg; set => Set(ref _rttAvg, value); }
    public string Loss { get => _loss; set => Set(ref _loss, value); }
    public string Ttl { get => _ttl; set => Set(ref _ttl, value); }
    public string OsGuess { get => _osGuess; set => Set(ref _osGuess, value); }
    public string DeviceType { get => _deviceType; set => Set(ref _deviceType, value); }
    public string OpenPorts { get => _openPorts; set => Set(ref _openPorts, value); }
    public int PortCount { get => _portCount; set => Set(ref _portCount, value); }
    public string Services { get => _services; set => Set(ref _services, value); }
    public string HttpTitle { get => _httpTitle; set => Set(ref _httpTitle, value); }
    public string HttpServer { get => _httpServer; set => Set(ref _httpServer, value); }
    public string TlsSubject { get => _tlsSubject; set => Set(ref _tlsSubject, value); }
    public string TlsIssuer { get => _tlsIssuer; set => Set(ref _tlsIssuer, value); }
    public string TlsExpiry { get => _tlsExpiry; set => Set(ref _tlsExpiry, value); }
    public string SshBanner { get => _sshBanner; set => Set(ref _sshBanner, value); }
    public string FtpBanner { get => _ftpBanner; set => Set(ref _ftpBanner, value); }
    public string SmtpBanner { get => _smtpBanner; set => Set(ref _smtpBanner, value); }
    public string RdpInfo { get => _rdpInfo; set => Set(ref _rdpInfo, value); }
    public string SnmpName { get => _snmpName; set => Set(ref _snmpName, value); }
    public string SnmpDescr { get => _snmpDescr; set => Set(ref _snmpDescr, value); }
    public string SnmpLocation { get => _snmpLocation; set => Set(ref _snmpLocation, value); }
    public string SnmpContact { get => _snmpContact; set => Set(ref _snmpContact, value); }
    public string SnmpUptime { get => _snmpUptime; set => Set(ref _snmpUptime, value); }
    public string MdnsName { get => _mdnsName; set => Set(ref _mdnsName, value); }
    public string UpnpDevice { get => _upnpDevice; set => Set(ref _upnpDevice, value); }
    public string UpnpServer { get => _upnpServer; set => Set(ref _upnpServer, value); }
    public string Shares { get => _shares; set => Set(ref _shares, value); }
    public string LoggedUser { get => _loggedUser; set => Set(ref _loggedUser, value); }
    public string WmiOs { get => _wmiOs; set => Set(ref _wmiOs, value); }
    public string WmiModel { get => _wmiModel; set => Set(ref _wmiModel, value); }
    public string WmiSerial { get => _wmiSerial; set => Set(ref _wmiSerial, value); }
    public string WmiUptime { get => _wmiUptime; set => Set(ref _wmiUptime, value); }
    public string WmiCpu { get => _wmiCpu; set => Set(ref _wmiCpu, value); }
    public string WmiRam { get => _wmiRam; set => Set(ref _wmiRam, value); }
    public string WmiDisks { get => _wmiDisks; set => Set(ref _wmiDisks, value); }
    public string Domain { get => _domain; set => Set(ref _domain, value); }
    public string Comment { get => _comment; set => Set(ref _comment, value); }
    public string LastSeen { get => _lastSeen; set => Set(ref _lastSeen, value); }
    public int ScanMs { get => _scanMs; set => Set(ref _scanMs, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }
    public string DotColor { get => _dotColor; set => Set(ref _dotColor, value); }

    public string NoteKey { get => _noteKey; set => Set(ref _noteKey, value); }

    public override string ToString() => IP;

    public void Apply(HostScanResult result)
    {
        IP = result.Address;

        NoteKey = result.Input;
        SortKey = SortableAddress(result.Address);
        Status = result.StatusText;
        StatusRank = (int)result.Status;
        Hostname = result.Hostname;
        NetBiosName = result.NetBiosName;
        Workgroup = result.Workgroup;
        Mac = result.Mac;
        Vendor = result.Vendor;
        RttMs = result.RoundTrip;
        RttAvg = result.AverageMs;
        Loss = result.Loss;
        Ttl = result.Ttl;
        OsGuess = result.OperatingSystem;
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
        MdnsName = result.MdnsName;
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
        Domain = result.Domain;
        ScanMs = result.ElapsedMs;
        Notes = result.Findings;
        DotColor = DotFor(result.Status);

        if (result.IsAlive)
        {
            LastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }
    }

    public static string DotFor(HostStatus status) => status switch
    {
        HostStatus.Online => "#FF2EA043",
        HostStatus.IcmpFiltered => "#FFE8A33D",
        HostStatus.Unreachable => "#FFD1444A",
        _ => "#FF9AA1AC"
    };

    public static long SortableAddress(string address)
    {
        if (!System.Net.IPAddress.TryParse(address, out System.Net.IPAddress? parsed))
        {
            return long.MaxValue;
        }

        byte[] octets = parsed.GetAddressBytes();
        if (octets.Length != 4)
        {
            return long.MaxValue - 1;
        }

        return ((long)octets[0] << 24) | ((long)octets[1] << 16) | ((long)octets[2] << 8) | octets[3];
    }
}
