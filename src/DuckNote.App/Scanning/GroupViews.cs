using DuckNote.Notes.Groups;

namespace DuckNote.App.Scanning;

/// <summary>I filtri di stato della tabella degli host.</summary>
public enum HostState
{
    All,
    Up,
    Down,
    Wait
}

/// <summary>
/// Un gruppo come lo mostra il pannello della pagina Rete: il nome, il colore, quanti host e in
/// che stato, se il monitoraggio li controlla e, per i gruppi propri, i bersagli.
/// </summary>
public sealed record GroupView(
    string Id,
    string Name,
    string Tone,
    string Icon,
    string Hint,
    int Total,
    int Up,
    int Down,
    int Wait,
    bool Monitor,
    bool Editable,
    IReadOnlyList<string> Targets);

/// <summary>
/// I gruppi del pannello della pagina Rete, in ordine: Tutti, Nota, i gruppi propri, Ricerca.
/// Niente interfaccia qui: lo usano le due app. Tutti e' l'interruttore del monitoraggio intero;
/// Nota e Ricerca hanno il loro nella nota (<see cref="ScanGroupFlags"/>); i gruppi propri il loro.
/// </summary>
public static class GroupViews
{
    /// <summary>Tutti gli host della tabella.</summary>
    public const string All = "*";

    /// <summary>Gli host trovati per intervallo fuori dalla nota e da ogni gruppo.</summary>
    public const string Found = "-";

    /// <summary>I colori dei gruppi propri, in giro.</summary>
    private static readonly string[] Tones = ["Teal", "Purple", "Indigo", "Pink", "Green", "Yellow", "Blue", "Red"];

    public static bool Matches(HostState state, HostRow row) => state switch
    {
        HostState.Up => row.IsUp,
        HostState.Down => row.IsDown,
        HostState.Wait => row.NeedsCheck,
        _ => true
    };

    public static bool InGroup(string id, HostRow row, GroupHosts groups) => id switch
    {
        All => true,
        ScanGroups.Note => row.FromNote,
        Found => !row.FromNote && !groups.InAny(row),
        _ => groups.Find(id) is { } group && groups.Contains(group, row)
    };

    /// <summary>Il colore di un gruppo, per il pannello e per i suoi host.</summary>
    public static string ToneOf(string id, GroupHosts groups) => id switch
    {
        All => "Blue",
        ScanGroups.Note => "Orange",
        Found => "Gray",
        _ => Tones[Math.Max(0, IndexOf(groups, id)) % Tones.Length]
    };

    /// <summary>
    /// I gruppi da mostrare, coi conteggi presi da <paramref name="rows"/> (gia' filtrate dalla
    /// ricerca, se c'e'). La Ricerca compare solo se ha host.
    /// </summary>
    public static IReadOnlyList<GroupView> Build(
        IReadOnlyList<HostRow> rows, GroupHosts groups, bool monitorAll, ScanGroupFlags flags)
    {
        List<GroupView> views =
        [
            View(All, "Tutti", "network", "Gli host della nota, dei gruppi e della ricerca", rows, monitorAll, editable: false, [], groups),
            View(ScanGroups.Note, "Nota", "note", "Gli indirizzi scritti nei fogli e sulla mappa", rows.Where(row => row.FromNote), flags.NoteMonitor, editable: false, [], groups),
        ];

        foreach (ScanGroup group in groups.All)
        {
            string hint = group.Targets.Count == 0 ? "Vuoto: aggiungi reti o indirizzi con Modifica" : string.Join(", ", group.Targets);
            views.Add(View(group.Id, group.Name, "group", hint, rows.Where(row => groups.Contains(group, row)), group.Monitor, editable: true, group.Targets, groups));
        }

        List<HostRow> found = [.. rows.Where(row => !row.FromNote && !groups.InAny(row))];

        if (found.Count > 0)
        {
            views.Add(View(Found, "Ricerca", "search", "Trovati per intervallo, fuori dalla nota e dai gruppi", found, flags.FoundMonitor, editable: false, [], groups));
        }

        return views;
    }

    /// <summary>Quanti host in ogni stato: per i numeri dei filtri, dentro il gruppo scelto.</summary>
    public static (int All, int Up, int Down, int Wait) Count(IEnumerable<HostRow> rows)
    {
        int all = 0, up = 0, down = 0, wait = 0;

        foreach (HostRow row in rows)
        {
            all++;
            up += row.IsUp ? 1 : 0;
            down += row.IsDown ? 1 : 0;
            wait += row.NeedsCheck ? 1 : 0;
        }

        return (all, up, down, wait);
    }

    private static GroupView View(
        string id, string name, string icon, string hint, IEnumerable<HostRow> rows, bool monitor, bool editable,
        IReadOnlyList<string> targets, GroupHosts groups)
    {
        (int all, int up, int down, int wait) = Count(rows);
        return new GroupView(id, name, ToneOf(id, groups), icon, hint, all, up, down, wait, monitor, editable, targets);
    }

    private static int IndexOf(GroupHosts groups, string id)
    {
        IReadOnlyList<ScanGroup> all = groups.All;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
