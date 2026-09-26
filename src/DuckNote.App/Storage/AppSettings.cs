using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuckNote.App.Storage;

public sealed class AppSettings
{
    public string Theme { get; set; } = "light";
    public bool FollowSystemTheme { get; set; } = true;

    public bool AutosaveEnabled { get; set; } = true;
    public int AutosaveDebounceMs { get; set; } = 1200;
    public bool LiveFormatting { get; set; } = true;
    public int FormatDebounceMs { get; set; } = 350;
    public int HostDebounceMs { get; set; } = 5000;

    public bool MonitorEnabled { get; set; } = true;
    public int MonitorIntervalSec { get; set; } = 60;

    public int MaxThreads { get; set; } = 64;
    public int PingCount { get; set; } = 2;
    public int PingTimeoutMs { get; set; } = 800;
    public int PortTimeoutMs { get; set; } = 500;
    public bool ScanDeadHosts { get; set; } = true;

    public bool ResolveDns { get; set; } = true;
    public string DnsServer { get; set; } = string.Empty;
    public bool ProbeNetBios { get; set; } = true;
    public bool ProbeMdns { get; set; } = true;
    public bool ProbeSsdp { get; set; } = true;
    public bool ProbeSnmp { get; set; } = true;
    public string SnmpCommunity { get; set; } = "public";
    public bool ProbeBanners { get; set; } = true;
    public bool ProbeShares { get; set; }
    public bool ProbeWmi { get; set; }

    public string Ports { get; set; } =
        "21,22,23,25,53,80,110,135,139,143,443,445,465,554,587,631,993,995,1433,1723,3000,3306," +
        "3389,5000,5060,5432,5900,5985,5986,6379,8000,8006,8080,8443,8888,9100,32400";

    public string LastRange { get; set; } = string.Empty;
    public string IgnoredHosts { get; set; } = string.Empty;

    public int SidebarWidth { get; set; } = 232;
    public int InspectorWidth { get; set; } = 330;
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 740;

    public bool DuckBackground { get; set; } = true;
    public int DuckCount { get; set; } = 22;
    public int DuckOpacity { get; set; } = 4;

    public int EditorZoom { get; set; } = 100;
    public string SidebarMode { get; set; } = "host";
    public bool ShowLineHighlight { get; set; } = true;

    public bool SecurityPrompted { get; set; }
    public int AutoLockMinutes { get; set; } = 15;

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.Settings))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings))
                ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.Ensure();
            File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(this, Format));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<int> PortList()
    {
        List<int> ports = [];
        foreach (string piece in Ports.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(piece.Trim(), out int port) && port is > 0 and <= 65535)
            {
                ports.Add(port);
            }
        }
        return ports.Count > 0 ? ports : DuckNote.Scan.ScanOptions.DefaultPorts;
    }

    public DuckNote.Scan.ScanOptions ToScanOptions() => new()
    {
        Concurrency = MaxThreads,
        PingCount = PingCount,
        PingTimeout = TimeSpan.FromMilliseconds(PingTimeoutMs),
        PortTimeout = TimeSpan.FromMilliseconds(PortTimeoutMs),
        Ports = PortList(),
        ScanDeadHosts = ScanDeadHosts,
        ResolveDns = ResolveDns,
        DnsServer = DnsServer,
        ProbeNetBios = ProbeNetBios,
        ProbeMdns = ProbeMdns,
        ProbeSsdp = ProbeSsdp,
        ProbeSnmp = ProbeSnmp,
        SnmpCommunity = SnmpCommunity,
        ProbeBanners = ProbeBanners,
        ProbeShares = ProbeShares,
        ProbeWmi = ProbeWmi
    };
}
