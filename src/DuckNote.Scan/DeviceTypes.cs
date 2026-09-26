using System.Text.RegularExpressions;

namespace DuckNote.Scan;

public static partial class DeviceTypes
{
    public static string Classify(
        IReadOnlyCollection<int> openPorts,
        string vendor,
        string snmpDescription,
        string upnp,
        string http,
        string operatingSystem)
    {
        HashSet<int> ports = [.. openPorts];
        string haystack = $"{vendor} {snmpDescription} {upnp} {http}".ToLowerInvariant();

        if (ports.Contains(9100) || ports.Contains(515) || ports.Contains(631))
        {
            return "Stampante";
        }
        if (ports.Contains(554) || CameraPattern().IsMatch(haystack))
        {
            return "Videocamera / NVR";
        }
        if (ports.Contains(8006))
        {
            return "Proxmox VE";
        }
        if (ports.Contains(902) || HypervisorPattern().IsMatch(haystack))
        {
            return "Host virtualizzazione";
        }
        if (ports.Contains(32400) || MediaPattern().IsMatch(haystack))
        {
            return "Media server";
        }
        if (ports.Contains(2049) || ports.Contains(548) || NasPattern().IsMatch(haystack))
        {
            return "NAS";
        }
        if (RouterPattern().IsMatch(haystack))
        {
            return "Router / Gateway";
        }
        if (SwitchPattern().IsMatch(haystack))
        {
            return "Switch gestito";
        }
        if (ports.Contains(5060) || VoipPattern().IsMatch(haystack))
        {
            return "VoIP";
        }
        if (ports.Contains(3389) || ports.Contains(5985) || ports.Contains(445)
            || operatingSystem.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Host Windows";
        }
        if (ports.Contains(22) && operatingSystem.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            return "Host Linux / Unix";
        }
        if (IotPattern().IsMatch(haystack))
        {
            return "Dispositivo IoT";
        }
        if (ports.Contains(80) || ports.Contains(443) || ports.Contains(8080))
        {
            return "Dispositivo con web UI";
        }
        return ports.Count > 0 ? "Host generico" : string.Empty;
    }

    [GeneratedRegex("hikvision|dahua|axis|camera|ipcam|nvr")]
    private static partial Regex CameraPattern();

    [GeneratedRegex("esxi|vmware")]
    private static partial Regex HypervisorPattern();

    [GeneratedRegex("plex|jellyfin|emby")]
    private static partial Regex MediaPattern();

    [GeneratedRegex("synology|qnap|nas|truenas")]
    private static partial Regex NasPattern();

    [GeneratedRegex("router|gateway|mikrotik|ubiquiti|fritz|openwrt|dd-wrt|pfsense|edgeos|ios software")]
    private static partial Regex RouterPattern();

    [GeneratedRegex(@"switch|catalyst|procurve|aruba|juniper|ex\d{4}")]
    private static partial Regex SwitchPattern();

    [GeneratedRegex("grandstream|yealink|polycom|snom|asterisk")]
    private static partial Regex VoipPattern();

    [GeneratedRegex("espressif|shelly|tasmota|sonoff|tuya|hue|nest|sonos|roku|chromecast")]
    private static partial Regex IotPattern();
}
