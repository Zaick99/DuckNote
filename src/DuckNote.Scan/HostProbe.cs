using System.Diagnostics;
using System.Net;
using DuckNote.Scan.Probes;
using DuckNote.Scan.Wire;

namespace DuckNote.Scan;

public sealed class HostProbe(ScanOptions options, IVendorLookup vendors)
{
    private static readonly int[] HttpPorts = [80, 8080, 8000, 8006, 5000, 3000, 8888, 9090, 10000];
    private static readonly int[] TlsPorts = [443, 8443, 5986, 9443];
    private static readonly int[] WinRmPorts = [5985, 5986, 135];

    public async Task<HostScanResult> InspectAsync(string target, CancellationToken cancellationToken = default)
    {
        Stopwatch clock = Stopwatch.StartNew();
        HostScanResult result = new() { Address = target, Input = target };

        try
        {
            await InspectCoreAsync(target, result, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Status = HostStatus.Error;
            result.Error = ex.Message;
        }

        result.ElapsedMs = (int)clock.ElapsedMilliseconds;
        return result;
    }

    private async Task InspectCoreAsync(string target, HostScanResult result, CancellationToken cancellationToken)
    {
        string address = await ResolveAsync(target, result, cancellationToken).ConfigureAwait(false);
        result.Address = address;

        await ReachAsync(address, result, cancellationToken).ConfigureAwait(false);
        if (!result.IsAlive && !options.ScanDeadHosts)
        {
            return;
        }

        HashSet<int> open = await ScanPortsAsync(address, result, cancellationToken).ConfigureAwait(false);
        if (!result.IsAlive)
        {
            return;
        }

        result.Deep = true;

        DeepFindings found = await GatherAsync(address, open, result, cancellationToken).ConfigureAwait(false);
        Apply(found, result);

        Conclude(result, open);
    }

    private async Task ReachAsync(string address, HostScanResult result, CancellationToken cancellationToken)
    {
        PingOutcome ping = await PingProbe
            .SendAsync(address, options.PingCount, options.PingTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (ping.Online)
        {
            result.Status = HostStatus.Online;
            result.AverageMs = ping.AverageMs;
            result.RoundTrip = ping.Describe();
            result.Loss = $"{ping.LossPercent}%";

            if (ping.Ttl > 0)
            {
                result.Ttl = ping.Ttl.ToString();
                result.OperatingSystem = TtlFingerprint.GuessOperatingSystem(ping.Ttl);
                result.Hops = TtlFingerprint.CountHops(ping.Ttl).ToString();
            }
        }

        result.Mac = ping.Online
            ? ArpProbe.FromCache(address)
            : await ArpProbe.ResolveAsync(address, cancellationToken).ConfigureAwait(false);

        if (result.Mac.Length > 0)
        {
            result.Vendor = vendors.Describe(result.Mac);
            if (!ping.Online)
            {
                result.Status = HostStatus.IcmpFiltered;
            }
        }
    }

    private async Task<HashSet<int>> ScanPortsAsync(
        string address, HostScanResult result, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> open = options.Ports.Count == 0
            ? []
            : await PortProbe.ScanAsync(address, options.Ports, options.PortTimeout, cancellationToken)
                             .ConfigureAwait(false);

        result.OpenPorts = open;
        if (open.Count > 0)
        {
            result.Services = ServiceNames.DescribeAll(open);
            if (!result.IsAlive)
            {
                result.Status = HostStatus.IcmpFiltered;
            }
        }

        return [.. open];
    }

    private async Task<DeepFindings> GatherAsync(
        string address, HashSet<int> open, HostScanResult result, CancellationToken cancellationToken)
    {
        Task<string> hostname = options.ResolveDns && result.Hostname.Length == 0
            ? NameProbe.ReverseAsync(address, options.DnsServer, TimeSpan.FromMilliseconds(900), cancellationToken)
            : Task.FromResult(string.Empty);

        Task<NetBiosMessage.NodeStatus> netBios = options.ProbeNetBios
            ? NameProbe.NetBiosAsync(address, TimeSpan.FromMilliseconds(700), cancellationToken)
            : Task.FromResult(NetBiosMessage.NodeStatus.Empty);

        Task<string> mdns = options.ProbeMdns
            ? NameProbe.MdnsAsync(address, TimeSpan.FromMilliseconds(600), cancellationToken)
            : Task.FromResult(string.Empty);

        Task<BannerFindings> banners = options.ProbeBanners
            ? ReadBannersAsync(address, open, cancellationToken)
            : Task.FromResult(BannerFindings.Empty);

        Task<IReadOnlyDictionary<string, string>> snmp = options.ProbeSnmp
            ? SnmpProbe.AskAsync(address, options.SnmpCommunity, TimeSpan.FromMilliseconds(900), cancellationToken)
            : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        Task<SsdpFindings> ssdp = options.ProbeSsdp
            ? SsdpProbe.AskAsync(address, TimeSpan.FromMilliseconds(800), cancellationToken)
            : Task.FromResult(SsdpFindings.Empty);

        Task<IReadOnlyList<string>> shares = options.ProbeShares && open.Contains(445)
            ? SharesProbe.ListAsync(address, TimeSpan.FromSeconds(4), cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>([]);

        Task<WindowsInventory> inventory = options.ProbeWmi && Array.Exists(WinRmPorts, open.Contains)
            ? WmiProbe.ReadAsync(address, TimeSpan.FromSeconds(8), cancellationToken)
            : Task.FromResult(WindowsInventory.Empty);

        await Task.WhenAll(hostname, netBios, mdns, banners, snmp, ssdp, shares, inventory).ConfigureAwait(false);

        return new DeepFindings(
            hostname.Result, netBios.Result, mdns.Result, banners.Result,
            snmp.Result, ssdp.Result, shares.Result, inventory.Result);
    }

    private async Task<BannerFindings> ReadBannersAsync(
        string address, HashSet<int> open, CancellationToken cancellationToken)
    {
        TimeSpan patience = options.BannerTimeout;
        int smtpPort = open.Contains(25) ? 25 : open.Contains(587) ? 587 : 0;
        int plainPort = Array.Find(HttpPorts, open.Contains);
        int securePort = Array.Find(TlsPorts, open.Contains);

        Task<string> ssh = Banner(address, open.Contains(22) ? 22 : 0, patience, cancellationToken);
        Task<string> ftp = Banner(address, open.Contains(21) ? 21 : 0, patience, cancellationToken);
        Task<string> smtp = Banner(address, smtpPort, patience, cancellationToken);

        Task<HttpFindings> plain = plainPort == 0
            ? Task.FromResult(HttpFindings.Empty)
            : HttpProbe.AskAsync(address, plainPort, useTls: false, TimeSpan.FromMilliseconds(1500), cancellationToken);

        Task<HttpFindings> secure = securePort == 0
            ? Task.FromResult(HttpFindings.Empty)
            : HttpProbe.AskAsync(address, securePort, useTls: true, TimeSpan.FromMilliseconds(2000), cancellationToken);

        await Task.WhenAll(ssh, ftp, smtp, plain, secure).ConfigureAwait(false);

        return new BannerFindings(
            ssh.Result, ftp.Result, smtp.Result, open.Contains(3389), plain.Result, secure.Result);
    }

    private static async Task<string> Banner(
        string address, int port, TimeSpan patience, CancellationToken cancellationToken)
    {
        if (port == 0)
        {
            return string.Empty;
        }

        string raw = await BannerProbe.ReadAsync(address, port, patience, null, cancellationToken).ConfigureAwait(false);
        return TextSanitiser.Clean(raw, 120);
    }

    private void Apply(DeepFindings found, HostScanResult result)
    {
        if (found.Hostname.Length > 0)
        {
            result.Hostname = found.Hostname;
        }

        result.NetBiosName = found.NetBios.Name;
        result.Workgroup = found.NetBios.Workgroup;
        if (result.Mac.Length == 0 && found.NetBios.Mac.Length > 0)
        {
            result.Mac = found.NetBios.Mac;
            result.Vendor = vendors.Describe(result.Mac);
        }
        if (found.NetBios.Users.Count > 0)
        {
            result.LoggedUser = string.Join(", ", found.NetBios.Users);
        }

        result.MdnsName = found.Mdns;

        result.SshBanner = found.Banners.Ssh;
        result.FtpBanner = found.Banners.Ftp;
        result.SmtpBanner = found.Banners.Smtp;
        result.RdpInfo = found.Banners.Rdp ? "RDP in ascolto" : string.Empty;
        ApplyWeb(found.Banners, result);

        result.SnmpDescription = Read(found.Snmp, "Description");
        result.SnmpObjectId = Read(found.Snmp, "ObjectId");
        result.SnmpUptime = Read(found.Snmp, "Uptime");
        result.SnmpContact = Read(found.Snmp, "Contact");
        result.SnmpName = Read(found.Snmp, "Name");
        result.SnmpLocation = Read(found.Snmp, "Location");

        result.UpnpServer = found.Ssdp.Server;
        result.UpnpDevice = found.Ssdp.Device;
        result.UpnpLocation = found.Ssdp.Location;

        result.Shares = string.Join(", ", found.Shares);

        result.WmiOs = found.Inventory.OperatingSystem;
        result.WmiModel = found.Inventory.Model;
        result.WmiSerial = found.Inventory.Serial;
        result.WmiUptime = found.Inventory.Uptime;
        result.WmiCpu = found.Inventory.Cpu;
        result.WmiRam = found.Inventory.Ram;
        result.WmiDisks = found.Inventory.Disks;

        if (found.Inventory.User.Length > 0)
        {
            result.LoggedUser = found.Inventory.User;
        }
        if (found.Inventory.Domain.Length > 0)
        {
            result.Domain = found.Inventory.Domain;
        }
        if (found.Inventory.OperatingSystem.Length > 0)
        {
            result.OperatingSystem = found.Inventory.OperatingSystem;
        }
    }

    private static void ApplyWeb(BannerFindings banners, HostScanResult result)
    {
        result.HttpTitle = banners.Plain.Title;
        result.HttpServer = banners.Plain.DescribeServer();
        if (result.HttpTitle.Length == 0 && banners.Plain.Redirect.Length > 0)
        {
            result.HttpTitle = "-> " + banners.Plain.Redirect;
        }

        result.TlsSubject = banners.Secure.TlsSubject;
        result.TlsIssuer = banners.Secure.TlsIssuer;
        result.TlsExpiry = banners.Secure.TlsExpiry;
        result.TlsProtocol = banners.Secure.TlsProtocol;

        if (result.HttpTitle.Length == 0)
        {
            result.HttpTitle = banners.Secure.Title;
        }
        if (result.HttpServer.Length == 0)
        {
            result.HttpServer = banners.Secure.DescribeServer();
        }
    }

    private static void Conclude(HostScanResult result, HashSet<int> open)
    {
        if (result.Vendor.Length == 0 || result.Vendor == "MAC locale / randomizzato")
        {
            string haystack = string.Join(' ',
                result.SnmpDescription, result.SnmpName, result.UpnpServer, result.UpnpDevice,
                result.HttpServer, result.HttpTitle, result.SshBanner, result.FtpBanner,
                result.SmtpBanner, result.MdnsName, result.Hostname, result.NetBiosName,
                result.TlsSubject, result.TlsIssuer);

            string guess = VendorFromText.Identify(haystack);
            if (guess.Length > 0)
            {
                result.Vendor = guess + " (da banner)";
            }
        }

        if (result.Domain.Length == 0 && result.Workgroup.Length > 0)
        {
            result.Domain = result.Workgroup;
        }

        result.OperatingSystem = OperatingSystemGuess.Refine(
            result.OperatingSystem, result.SnmpDescription, result.SshBanner);

        result.DeviceType = DeviceTypes.Classify(
            open, result.Vendor, result.SnmpDescription,
            $"{result.UpnpServer} {result.UpnpDevice}",
            $"{result.HttpServer} {result.HttpTitle}",
            result.OperatingSystem);

        result.Findings = SecurityFindings.Describe(open, result.TlsExpiry);
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string field) =>
        values.TryGetValue(field, out string? value) ? value : string.Empty;

    private async Task<string> ResolveAsync(string target, HostScanResult result, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(target, out _))
        {
            return target;
        }

        result.Hostname = target;
        string resolved = await NameProbe
            .ResolveAsync(target, options.DnsServer, TimeSpan.FromMilliseconds(1500), cancellationToken)
            .ConfigureAwait(false);

        return resolved.Length > 0 ? resolved : target;
    }

    private sealed record DeepFindings(
        string Hostname,
        NetBiosMessage.NodeStatus NetBios,
        string Mdns,
        BannerFindings Banners,
        IReadOnlyDictionary<string, string> Snmp,
        SsdpFindings Ssdp,
        IReadOnlyList<string> Shares,
        WindowsInventory Inventory);

    private sealed record BannerFindings(
        string Ssh, string Ftp, string Smtp, bool Rdp, HttpFindings Plain, HttpFindings Secure)
    {
        public static readonly BannerFindings Empty =
            new("", "", "", false, HttpFindings.Empty, HttpFindings.Empty);
    }
}
