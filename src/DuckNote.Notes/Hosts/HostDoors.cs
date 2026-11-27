using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace DuckNote.Notes.Hosts;

/// <summary>
/// Una cosa che si apre su un host, dalla pagina Rete: una pagina web, le condivisioni, un
/// terminale SSH, il desktop remoto. Kind dice come aprirla (web, share, ssh, rdp); Target e'
/// l'indirizzo da aprire (per rdp, l'host da dare al client).
/// </summary>
public sealed record HostDoor(string Kind, string Label, string Target);

/// <summary>
/// Le porte aperte di un host diventano comandi: 443 e' una pagina https, 80 una pagina http,
/// 445 le condivisioni, 22 un terminale, 3389 il desktop remoto. Solo quello che l'host ha
/// davvero aperto, e solo quello che il sistema sa aprire da se'.
/// </summary>
public static partial class HostDoors
{
    /// <summary>Le porte web conosciute e il loro protocollo: prima le sicure, poi le altre.</summary>
    private static readonly (int Port, string Scheme)[] Web =
    [
        (443, "https"), (80, "http"), (8443, "https"), (8006, "https"), (9090, "https"), (10000, "https"),
        (5001, "https"), (8080, "http"), (8000, "http"), (5000, "http"), (3000, "http"), (8888, "http")
    ];

    /// <param name="address">L'indirizzo dell'host (un IP, o un nome).</param>
    /// <param name="openPorts">Le porte aperte, come le scrive la tabella ("22, 80, 443").</param>
    /// <param name="mac">true sul Mac (SSH apre il Terminale, le condivisioni il Finder), false su Windows
    /// (le condivisioni in Esplora risorse, il desktop remoto con mstsc).</param>
    public static IReadOnlyList<HostDoor> For(string address, string openPorts, bool mac)
    {
        string host = address.Trim();

        if (host.Length == 0)
        {
            return [];
        }

        HashSet<int> open = [.. Ports(openPorts)];
        List<HostDoor> doors = [];
        string authority = IPAddress.TryParse(host, out IPAddress? ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;

        foreach ((int port, string scheme) in Web)
        {
            if (!open.Contains(port))
            {
                continue;
            }

            bool usual = (scheme == "https" && port == 443) || (scheme == "http" && port == 80);
            string url = usual ? $"{scheme}://{authority}/" : $"{scheme}://{authority}:{port.ToString(CultureInfo.InvariantCulture)}/";
            doors.Add(new HostDoor("web", url.TrimEnd('/'), url));
        }

        if (open.Contains(445) || open.Contains(139))
        {
            doors.Add(mac
                ? new HostDoor("share", "Condivisioni (Finder)", $"smb://{authority}/")
                : new HostDoor("share", "Condivisioni (Esplora risorse)", $@"\\{host}"));
        }

        if (open.Contains(22) && mac)
        {
            doors.Add(new HostDoor("ssh", "Terminale SSH", $"ssh://{authority}"));
        }

        if (open.Contains(3389) && !mac)
        {
            doors.Add(new HostDoor("rdp", "Desktop remoto", host));
        }

        return doors;
    }

    private static IEnumerable<int> Ports(string? written) =>
        Number().Matches(written ?? string.Empty)
            .Select(match => int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : 0)
            .Where(port => port is > 0 and < 65536);

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
