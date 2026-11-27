using DuckNote.Notes.Groups;

namespace DuckNote.App.Scanning;

/// <summary>
/// I gruppi di scansione visti dalla tabella degli host: chi sta in quale gruppo, quali indirizzi
/// dei gruppi col monitoraggio acceso vanno tenuti in tabella e controllati. Niente WPF qui: lo
/// usano le due app (e la mappa, per la sua libreria "Dalla rete").
/// </summary>
public sealed class GroupHosts
{
    private readonly List<(ScanGroup Group, TargetMatcher Matcher)> _groups;

    public GroupHosts(IReadOnlyList<ScanGroup> groups)
    {
        _groups = [.. groups.Select(group => (group, new TargetMatcher(group.Targets)))];
    }

    public IReadOnlyList<ScanGroup> All => [.. _groups.Select(pair => pair.Group)];

    public ScanGroup? Find(string? id) => _groups.FirstOrDefault(pair => pair.Group.Id == id).Group;

    public bool Contains(ScanGroup group, HostRow row) =>
        _groups.FirstOrDefault(pair => pair.Group.Id == group.Id).Matcher is { } matcher && Matches(matcher, row);

    /// <summary>Gli identificativi dei gruppi (propri) a cui appartiene l'host.</summary>
    public IReadOnlyList<string> GroupsOf(HostRow row) =>
        [.. _groups.Where(pair => Matches(pair.Matcher, row)).Select(pair => pair.Group.Id)];

    /// <summary>L'host sta in almeno un gruppo proprio.</summary>
    public bool InAny(HostRow row) => _groups.Any(pair => Matches(pair.Matcher, row));

    /// <summary>
    /// Gli IP singoli e i nomi dei gruppi col monitoraggio acceso: stanno in tabella e si
    /// controllano da soli, come gli indirizzi scritti nella nota.
    /// </summary>
    public IReadOnlyList<string> WatchedSingles() =>
        [.. _groups.Where(pair => pair.Group.Monitor).SelectMany(pair => pair.Matcher.Singles).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Gli host gia' in tabella che cadono nelle reti dei gruppi col monitoraggio acceso.</summary>
    public IReadOnlyList<string> MonitoredRows(IEnumerable<HostRow> rows) =>
        [.. rows.Where(row => row.Address.Length > 0 && _groups.Any(pair => pair.Group.Monitor && Matches(pair.Matcher, row)))
            .Select(row => row.Address)];

    /// <summary>Quanti host della tabella stanno nel gruppo.</summary>
    public int Count(ScanGroup group, IEnumerable<HostRow> rows) => rows.Count(row => Contains(group, row));

    private static bool Matches(TargetMatcher matcher, HostRow row) => matcher.Matches(row.IP, row.Shown, row.NoteKey, row.Name);
}
