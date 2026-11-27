using DuckNote.Notes.Groups;

namespace DuckNote.App.Scanning;

/// <summary>
/// Gli host della tabella come li vuole la pagina della mappa (dm.hosts): chiave (com'e'
/// scritto, o l'IP), IP, nome, MAC, marca, tipo, sistema, stato, risposta e i gruppi in cui sta
/// ("nota" per quelli scritti nella nota). Serve alla libreria "Dalla rete" e al colore degli
/// apparati. Niente WPF qui: lo usano le due app.
/// </summary>
public static class MapFeed
{
    /// <summary>Oltre questo numero la libreria non serve piu' a scegliere: si tengono i primi.</summary>
    private const int Most = 4000;

    public static List<Dictionary<string, object>> Hosts(ScanSession session, GroupHosts groups)
    {
        List<Dictionary<string, object>> list = [];

        foreach (HostRow row in session.Rows)
        {
            if (list.Count >= Most)
            {
                break;
            }

            if (row.Shown.Length == 0 || session.IsHidden(row.Shown))
            {
                continue;
            }

            List<string> member = [.. groups.GroupsOf(row)];

            if (row.FromNote)
            {
                member.Insert(0, ScanGroups.Note);
            }

            list.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["key"] = row.Shown,
                ["ip"] = row.IP,
                ["name"] = row.Label,
                ["mac"] = row.Mac,
                ["vendor"] = row.Vendor,
                ["type"] = row.DeviceType,
                ["os"] = row.Os,
                ["state"] = row.IsUp ? "up" : row.IsDown ? "down" : row.IsChecked ? "wait" : "unknown",
                ["rtt"] = row.IsUp ? row.RoundTripShort : string.Empty,
                ["groups"] = member
            });
        }

        return list;
    }

    /// <summary>I gruppi della nota per il selettore della libreria: identificativo, nome e monitoraggio.</summary>
    public static List<Dictionary<string, object>> Groups(GroupHosts groups) =>
    [
        .. groups.All.Select(group => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = group.Id,
            ["name"] = group.Name,
            ["monitor"] = group.Monitor
        })
    ];
}
