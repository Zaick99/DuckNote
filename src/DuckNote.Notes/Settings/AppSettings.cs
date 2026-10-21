using System.Text.Json;
using System.Text.Json.Serialization;
using DuckNote.Scan;

namespace DuckNote.Notes.Settings;

/// <summary>
/// Le impostazioni, in settings.json. I nomi delle proprieta' sono il formato del file: chi
/// ha gia' un settings.json lo ritrova com'era. Resta in chiaro anche con la cifratura,
/// perche' la finestra si costruisce da qui prima che esista una chiave.
/// </summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "light";
    public bool FollowSystemTheme { get; set; } = true;

    public bool AutosaveEnabled { get; set; } = true;
    public int AutosaveDebounceMs { get; set; } = 1200;
    public int FormatDebounceMs { get; set; } = 350;
    public int HostDebounceMs { get; set; } = 5000;

    public bool MonitorEnabled { get; set; } = true;
    public int MonitorIntervalSec { get; set; } = 60;

    public int MaxThreads { get; set; } = 256;
    public int MaxSockets { get; set; } = 900;
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
    public int WindowWidth { get; set; } = 1240;
    public int WindowHeight { get; set; } = 800;

    public bool DuckBackground { get; set; } = true;
    public int DuckCount { get; set; } = 22;
    public int DuckOpacity { get; set; } = 4;

    public int EditorZoom { get; set; } = 100;
    public bool ShowLineHighlight { get; set; } = true;

    public bool SecurityPrompted { get; set; }
    public int AutoLockMinutes { get; set; } = 15;

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Preferenza di tema per ThemeManager: system, light o dark.</summary>
    [JsonIgnore]
    public string ThemePreference => FollowSystemTheme ? "system" : Theme == "dark" ? "dark" : "light";

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.Settings;

        try
        {
            AppSettings loaded = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();

            return loaded.Sane();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Scritto accanto e poi spostato: un'interruzione a meta' lascia il file di prima, non uno
    /// troncato che alla prossima lettura riporterebbe tutto ai valori di fabbrica.
    /// </summary>
    public bool Save(string? path = null)
    {
        path ??= AppPaths.Settings;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Format));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Un settings.json scritto a mano non puo' rompere niente: ogni numero torna nel suo intervallo.</summary>
    public AppSettings Sane()
    {
        Theme = Theme == "dark" ? "dark" : "light";
        AutosaveDebounceMs = Math.Clamp(AutosaveDebounceMs, 200, 60000);
        MonitorIntervalSec = Math.Clamp(MonitorIntervalSec, 10, 86400);
        MaxThreads = Math.Clamp(MaxThreads, 1, 1024);
        MaxSockets = Math.Clamp(MaxSockets, 16, 4096);
        PingCount = Math.Clamp(PingCount, 1, 10);
        PingTimeoutMs = Math.Clamp(PingTimeoutMs, 100, 10000);
        PortTimeoutMs = Math.Clamp(PortTimeoutMs, 50, 10000);
        DuckCount = Math.Clamp(DuckCount, 0, 200);
        DuckOpacity = Math.Clamp(DuckOpacity, 0, 100);
        EditorZoom = Math.Clamp(EditorZoom, 50, 300);
        AutoLockMinutes = Math.Clamp(AutoLockMinutes, 0, 1440);
        SidebarWidth = Math.Clamp(SidebarWidth, 170, 600);
        InspectorWidth = Math.Clamp(InspectorWidth, 260, 700);
        DnsServer ??= string.Empty;
        SnmpCommunity ??= "public";
        Ports ??= string.Empty;
        LastRange ??= string.Empty;
        IgnoredHosts ??= string.Empty;

        return this;
    }

    public IReadOnlyList<int> PortList()
    {
        List<int> ports = [];

        foreach (string piece in Ports.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(piece.Trim(), out int port) && port is > 0 and <= 65535 && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports.Count > 0 ? ports : ScanOptions.DefaultPorts;
    }

    public ScanOptions ToScanOptions() => new()
    {
        Concurrency = MaxThreads,
        Sockets = MaxSockets,
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
