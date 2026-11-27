using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DuckNote.Notes.Calendar;

/// <summary>I tipi di voce del calendario, come stanno nel file: appuntamento, da fare, scadenza, appunto.</summary>
public static class AgendaKinds
{
    public const string Event = "event";
    public const string Todo = "todo";
    public const string Deadline = "deadline";
    public const string Note = "note";

    public static bool IsKnown(string kind) => kind is Event or Todo or Deadline or Note;

    public static string Label(string kind) => kind switch
    {
        Todo => "Da fare",
        Deadline => "Scadenza",
        Note => "Appunto",
        _ => "Appuntamento"
    };
}

/// <summary>
/// Il legame di una voce con la rete: un host (com'e' scritto nella nota o il suo IP) o un
/// apparato della mappa (mappa, apparato, e il suo nome e indirizzo quando e' stato scelto).
/// </summary>
public sealed record AgendaLink(string Kind, string Host, string Map = "", string Node = "", string Name = "")
{
    public const string HostKind = "host";
    public const string NodeKind = "node";

    /// <summary>Come si legge il legame: il nome, se c'e', altrimenti l'indirizzo.</summary>
    public string Shown => Name.Length > 0 ? Name : Host;
}

/// <summary>Una voce del calendario. Le scadenze dei certificati sono voci "virtuali": nascono dalle analisi, non si salvano come voci.</summary>
public sealed class AgendaItem
{
    public required string Id { get; init; }
    public string Kind { get; set; } = AgendaKinds.Event;
    public string Title { get; set; } = string.Empty;
    public DateOnly Date { get; set; }

    /// <summary>L'ora; null per le voci di tutto il giorno.</summary>
    public TimeOnly? Time { get; set; }

    public string Notes { get; set; } = string.Empty;

    /// <summary>I promemoria, in minuti prima dell'inizio (per le voci di tutto il giorno: prima delle 9).</summary>
    public List<int> Remind { get; set; } = [];

    /// <summary>"" (una volta), "day", "week", "month", "year".</summary>
    public string Repeat { get; set; } = string.Empty;

    /// <summary>Ogni quanti giorni, settimane, mesi o anni.</summary>
    public int Every { get; set; } = 1;

    /// <summary>L'ultimo giorno della ripetizione (compreso); null senza fine.</summary>
    public DateOnly? Until { get; set; }

    /// <summary>Le volte fatte (per una voce che non si ripete: la sua data).</summary>
    public SortedSet<DateOnly> Done { get; } = [];

    /// <summary>Le volte tolte da una ripetizione ("elimina solo questa").</summary>
    public SortedSet<DateOnly> Skip { get; } = [];

    public AgendaLink? Link { get; set; }

    /// <summary>La scadenza di un certificato vista da un'analisi.</summary>
    public bool Certificate { get; init; }
}

/// <summary>Un certificato visto da un'analisi: l'host, quando scade, chi e' e chi l'ha firmato.</summary>
public sealed record CertificateSeen(string Host, DateOnly Expires, string Subject = "", string Issuer = "", string Name = "");

/// <summary>Una volta di una voce: il giorno, l'inizio e la chiave che la distingue (id@giorno).</summary>
public sealed record Occurrence(AgendaItem Item, DateOnly Date)
{
    public string Key => Item.Id + "@" + Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public bool AllDay => Item.Time is null;

    public bool IsDone => Item.Done.Contains(Date);

    /// <summary>L'inizio; per le voci di tutto il giorno, la mezzanotte.</summary>
    public DateTime Start => Date.ToDateTime(Item.Time ?? TimeOnly.MinValue);

    /// <summary>Il momento da cui si contano i promemoria: l'ora, o le 9 per le voci di tutto il giorno.</summary>
    public DateTime Anchor => Date.ToDateTime(Item.Time ?? Agenda.AllDayAt);
}

/// <summary>Un promemoria che scatta: la volta, quanti minuti prima e quando.</summary>
public sealed record AgendaAlert(Occurrence Occurrence, int Before, DateTime At);

/// <summary>Il riepilogo all'apertura: in ritardo, oggi, nei prossimi 7 giorni, certificati entro 30 giorni.</summary>
public sealed record AgendaSummary(
    IReadOnlyList<Occurrence> Overdue,
    IReadOnlyList<Occurrence> Today,
    IReadOnlyList<Occurrence> Week,
    IReadOnlyList<Occurrence> Certificates)
{
    public bool IsEmpty => Overdue.Count == 0 && Today.Count == 0 && Week.Count == 0 && Certificates.Count == 0;
}

/// <summary>
/// Il calendario di una nota, come sta nel file della nota (calendario.json, cifrato con lei):
/// { "v": 1, "items": [ voci ], "certs": { "off", "remind", "known": [...], "ignored": [...], "done": [...] } }.
/// Le voci si ripetono per giorni, settimane, mesi o anni; i promemoria sono minuti prima
/// dell'inizio. Le scadenze dei certificati arrivano dalle analisi e diventano voci virtuali.
/// </summary>
public sealed class Agenda
{
    /// <summary>L'ora a cui suonano i promemoria delle voci di tutto il giorno.</summary>
    public static readonly TimeOnly AllDayAt = new(9, 0);

    /// <summary>I promemoria dei certificati, se nessuno li ha cambiati: 30 giorni, 7 giorni, 1 giorno prima.</summary>
    public static readonly IReadOnlyList<int> CertificateDefaults = [43200, 10080, 1440];

    public const string CertificatePrefix = "cert:";

    private const int MostItems = 5000;
    private const int MostCertificates = 500;
    private const int MostDates = 2000;
    private const int MostReminders = 8;
    private const int LongestReminder = 525600;

    public List<AgendaItem> Items { get; } = [];

    public bool CertificatesOff { get; set; }

    public List<int> CertificateRemind { get; set; } = [.. CertificateDefaults];

    public List<CertificateSeen> Certificates { get; } = [];

    public SortedSet<string> IgnoredHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Le scadenze di certificato segnate come fatte (rinnovate): "host@giorno".</summary>
    public SortedSet<string> CertificatesDone { get; } = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ file

    public static Agenda Read(byte[]? json)
    {
        Agenda agenda = new();

        if (json is not { Length: > 0 })
        {
            return agenda;
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return agenda;
        }

        if (root is null)
        {
            return agenda;
        }

        HashSet<string> ids = new(StringComparer.Ordinal);

        if (root["items"] is JsonArray items)
        {
            foreach (JsonNode? node in items.Take(MostItems))
            {
                if (node is JsonObject written && ReadItem(written) is { } item)
                {
                    if (!ids.Add(item.Id))
                    {
                        item = Copy(item, NewId());
                        ids.Add(item.Id);
                    }

                    agenda.Items.Add(item);
                }
            }
        }

        if (root["certs"] is JsonObject certs)
        {
            agenda.CertificatesOff = Flag(certs["off"]);

            if (certs["remind"] is JsonArray remind)
            {
                agenda.CertificateRemind = Reminders(remind);
            }

            if (certs["known"] is JsonArray known)
            {
                foreach (JsonNode? node in known.Take(MostCertificates))
                {
                    if (node is JsonObject seen && Clean(Text(seen["host"]), 253) is { Length: > 0 } host && Day(Text(seen["date"])) is { } expires
                        && !agenda.Certificates.Any(other => other.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
                    {
                        agenda.Certificates.Add(new CertificateSeen(host, expires, Clean(Text(seen["subject"]), 300), Clean(Text(seen["issuer"]), 300), Clean(Text(seen["name"]), 120)));
                    }
                }
            }

            if (certs["ignored"] is JsonArray ignored)
            {
                agenda.IgnoredHosts.UnionWith(ignored.Take(MostCertificates).Select(Text).Select(host => Clean(host, 253)).Where(host => host.Length > 0));
            }

            if (certs["done"] is JsonArray done)
            {
                agenda.CertificatesDone.UnionWith(done.Take(MostDates).Select(Text).Where(key => key.Length is > 0 and <= 270 && key.Contains('@')));
            }
        }

        return agenda;
    }

    /// <summary>Il calendario per il file della nota; null se e' vuoto e con le impostazioni di serie (la nota resta com'era).</summary>
    public byte[]? Write()
    {
        bool plain = Items.Count == 0 && Certificates.Count == 0 && IgnoredHosts.Count == 0 && CertificatesDone.Count == 0
                     && !CertificatesOff && CertificateRemind.SequenceEqual(CertificateDefaults);
        return plain ? null : JsonSerializer.SerializeToUtf8Bytes(ToJson());
    }

    /// <summary>Il calendario intero come lo vuole la pagina (anche vuoto).</summary>
    public string Json() => ToJson().ToJsonString();

    private JsonObject ToJson()
    {
        JsonArray items = [];

        foreach (AgendaItem item in Items)
        {
            items.Add(WriteItem(item));
        }

        JsonArray known = [];

        foreach (CertificateSeen seen in Certificates)
        {
            JsonObject cert = new() { ["host"] = seen.Host, ["date"] = Iso(seen.Expires) };
            AddText(cert, "subject", seen.Subject);
            AddText(cert, "issuer", seen.Issuer);
            AddText(cert, "name", seen.Name);
            known.Add(cert);
        }

        return new JsonObject
        {
            ["v"] = 1,
            ["items"] = items,
            ["certs"] = new JsonObject
            {
                ["off"] = CertificatesOff,
                ["remind"] = new JsonArray([.. CertificateRemind.Select(minutes => (JsonNode)minutes)]),
                ["known"] = known,
                ["ignored"] = new JsonArray([.. IgnoredHosts.Select(host => (JsonNode)host)]),
                ["done"] = new JsonArray([.. CertificatesDone.Select(key => (JsonNode)key)])
            }
        };
    }

    /// <summary>Una voce dal file o dalla pagina; null se non ha un giorno valido.</summary>
    public static AgendaItem? ReadItem(JsonObject written)
    {
        if (Day(Text(written["date"])) is not { } date)
        {
            return null;
        }

        string id = Text(written["id"]);
        string kind = Text(written["kind"]);
        string repeat = Text(written["repeat"]);

        AgendaItem item = new()
        {
            Id = IsId(id) ? id : NewId(),
            Kind = AgendaKinds.IsKnown(kind) ? kind : AgendaKinds.Event,
            Title = Clean(Text(written["title"]), 200),
            Date = date,
            Time = Hour(Text(written["time"])),
            Notes = CleanNotes(Text(written["notes"])),
            Remind = written["remind"] is JsonArray remind ? Reminders(remind) : [],
            Repeat = repeat is "day" or "week" or "month" or "year" ? repeat : string.Empty,
            Every = written["every"] is JsonValue every && every.TryGetValue(out int n) ? Math.Clamp(n, 1, 99) : 1,
            Link = written["link"] is JsonObject link ? ReadLink(link) : null
        };

        if (item.Repeat.Length > 0 && Day(Text(written["until"])) is { } until && until >= date)
        {
            item.Until = until;
        }

        if (written["done"] is JsonArray done)
        {
            item.Done.UnionWith(done.Take(MostDates).Select(day => Day(Text(day))).OfType<DateOnly>().Where(day => day >= date));
        }

        if (written["skip"] is JsonArray skip && item.Repeat.Length > 0)
        {
            item.Skip.UnionWith(skip.Take(MostDates).Select(day => Day(Text(day))).OfType<DateOnly>().Where(day => day >= date));
        }

        return item;
    }

    public static JsonObject WriteItem(AgendaItem item)
    {
        JsonObject written = new()
        {
            ["id"] = item.Id,
            ["kind"] = item.Kind,
            ["title"] = item.Title,
            ["date"] = Iso(item.Date)
        };

        if (item.Time is { } time)
        {
            written["time"] = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        AddText(written, "notes", item.Notes);

        if (item.Remind.Count > 0)
        {
            written["remind"] = new JsonArray([.. item.Remind.Select(minutes => (JsonNode)minutes)]);
        }

        if (item.Repeat.Length > 0)
        {
            written["repeat"] = item.Repeat;

            if (item.Every > 1)
            {
                written["every"] = item.Every;
            }

            if (item.Until is { } until)
            {
                written["until"] = Iso(until);
            }

            if (item.Skip.Count > 0)
            {
                written["skip"] = new JsonArray([.. item.Skip.Select(day => (JsonNode)Iso(day))]);
            }
        }

        if (item.Done.Count > 0)
        {
            written["done"] = new JsonArray([.. item.Done.Select(day => (JsonNode)Iso(day))]);
        }

        if (item.Link is { } link)
        {
            JsonObject linked = new() { ["kind"] = link.Kind };
            AddText(linked, "host", link.Host);
            AddText(linked, "map", link.Map);
            AddText(linked, "node", link.Node);
            AddText(linked, "name", link.Name);
            written["link"] = linked;
        }

        return written;
    }

    private static AgendaLink? ReadLink(JsonObject link)
    {
        string kind = Text(link["kind"]);
        string host = Clean(Text(link["host"]), 253);
        string name = Clean(Text(link["name"]), 120);

        if (kind == AgendaLink.NodeKind)
        {
            string map = Clean(Text(link["map"]), 40);
            string node = Clean(Text(link["node"]), 40);
            return node.Length > 0 ? new AgendaLink(AgendaLink.NodeKind, host, map, node, name) : null;
        }

        return host.Length > 0 ? new AgendaLink(AgendaLink.HostKind, host, Name: name) : null;
    }

    // ------------------------------------------------------------------ le volte

    /// <summary>
    /// I giorni in cui cade una voce fra <paramref name="from"/> e <paramref name="to"/> (compresi),
    /// senza quelli tolti. I mesi corti tengono l'ultimo giorno (il 31 diventa il 30, o il 28):
    /// ogni volta si conta dal primo giorno, cosi' il mese dopo torna il 31.
    /// </summary>
    public static IEnumerable<DateOnly> Dates(AgendaItem item, DateOnly from, DateOnly to)
    {
        DateOnly start = item.Date;
        DateOnly last = item.Until is { } until && until < to ? until : to;

        if (last < from || start > last)
        {
            yield break;
        }

        int every = Math.Clamp(item.Every, 1, 99);

        switch (item.Repeat)
        {
            case "day" or "week":
            {
                int step = every * (item.Repeat == "week" ? 7 : 1);
                int k = start >= from ? 0 : (from.DayNumber - start.DayNumber + step - 1) / step;

                for (int guard = 0; guard < MostDates * 4; guard++, k++)
                {
                    DateOnly day = start.AddDays(k * step);

                    if (day > last)
                    {
                        yield break;
                    }

                    if (!item.Skip.Contains(day))
                    {
                        yield return day;
                    }
                }

                yield break;
            }

            case "month" or "year":
            {
                int months = every * (item.Repeat == "year" ? 12 : 1);
                int gap = (from.Year - start.Year) * 12 + from.Month - start.Month;
                int k = Math.Max(0, gap / months - 1);

                for (int guard = 0; guard < MostDates * 4; guard++, k++)
                {
                    DateOnly day = start.AddMonths(k * months);

                    if (day > last)
                    {
                        yield break;
                    }

                    if (day >= from && !item.Skip.Contains(day))
                    {
                        yield return day;
                    }
                }

                yield break;
            }

            default:
                if (start >= from && start <= last)
                {
                    yield return start;
                }

                yield break;
        }
    }

    /// <summary>Le voci vere e quelle dei certificati (se non sono spente).</summary>
    public IEnumerable<AgendaItem> AllItems() => Items.Concat(CertificateItems());

    /// <summary>Le scadenze dei certificati come voci: tutto il giorno, coi promemoria dei certificati, legate all'host.</summary>
    public IEnumerable<AgendaItem> CertificateItems()
    {
        if (CertificatesOff)
        {
            yield break;
        }

        foreach (CertificateSeen seen in Certificates)
        {
            if (IgnoredHosts.Contains(seen.Host))
            {
                continue;
            }

            AgendaItem item = new()
            {
                Id = CertificatePrefix + seen.Host.ToLowerInvariant(),
                Kind = AgendaKinds.Deadline,
                Title = $"Scade il certificato di {seen.Host}",
                Date = seen.Expires,
                Notes = string.Join('\n', ((string[])[seen.Subject, seen.Issuer.Length > 0 ? "Emesso da " + seen.Issuer : string.Empty]).Where(line => line.Length > 0)),
                Remind = [.. CertificateRemind],
                Link = new AgendaLink(AgendaLink.HostKind, seen.Host, Name: seen.Name),
                Certificate = true
            };

            if (CertificatesDone.Contains(CertificateKey(seen.Host, seen.Expires)))
            {
                item.Done.Add(seen.Expires);
            }

            yield return item;
        }
    }

    /// <summary>Tutte le volte fra due giorni (compresi), in ordine di giorno e di ora (prima quelle di tutto il giorno).</summary>
    public IReadOnlyList<Occurrence> Between(DateOnly from, DateOnly to) =>
    [
        .. AllItems().SelectMany(item => Dates(item, from, to).Select(day => new Occurrence(item, day)))
            .OrderBy(occurrence => occurrence.Date)
            .ThenBy(occurrence => occurrence.Item.Time is null ? 0 : 1)
            .ThenBy(occurrence => occurrence.Item.Time ?? TimeOnly.MinValue)
            .ThenBy(occurrence => occurrence.Item.Title, StringComparer.CurrentCultureIgnoreCase)
    ];

    /// <summary>I promemoria che scattano dopo <paramref name="after"/> e fino a <paramref name="upTo"/> compreso, delle volte non ancora fatte.</summary>
    public IReadOnlyList<AgendaAlert> Alerts(DateTime after, DateTime upTo)
    {
        List<AgendaAlert> alerts = [];

        if (upTo <= after)
        {
            return alerts;
        }

        foreach (AgendaItem item in AllItems())
        {
            if (item.Remind.Count == 0)
            {
                continue;
            }

            DateOnly from = DateOnly.FromDateTime(after.AddMinutes(item.Remind.Min())).AddDays(-1);
            DateOnly to = DateOnly.FromDateTime(upTo.AddMinutes(item.Remind.Max())).AddDays(1);

            foreach (DateOnly day in Dates(item, from, to))
            {
                Occurrence occurrence = new(item, day);

                if (occurrence.IsDone)
                {
                    continue;
                }

                foreach (int before in item.Remind)
                {
                    DateTime at = occurrence.Anchor.AddMinutes(-before);

                    if (at > after && at <= upTo)
                    {
                        alerts.Add(new AgendaAlert(occurrence, before, at));
                    }
                }
            }
        }

        return [.. alerts.OrderBy(alert => alert.At)];
    }

    /// <summary>
    /// Il riepilogo di adesso: le cose da fare e le scadenze rimaste indietro (fino a 30 giorni
    /// fa), quello che c'e' oggi, quello dei prossimi 7 giorni e i certificati che scadono entro 30.
    /// </summary>
    public AgendaSummary Summary(DateTime now)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        List<Occurrence> overdue = [];
        List<Occurrence> todays = [];
        List<Occurrence> week = [];
        List<Occurrence> certificates = [];

        foreach (Occurrence occurrence in Between(today.AddDays(-30), today.AddDays(30)))
        {
            if (occurrence.IsDone)
            {
                continue;
            }

            if (occurrence.Item.Certificate && occurrence.Date >= today)
            {
                certificates.Add(occurrence);
            }

            if (occurrence.Date < today)
            {
                if (occurrence.Item.Kind is AgendaKinds.Todo or AgendaKinds.Deadline)
                {
                    overdue.Add(occurrence);
                }
            }
            else if (occurrence.Date == today)
            {
                todays.Add(occurrence);
            }
            else if (occurrence.Date <= today.AddDays(7))
            {
                week.Add(occurrence);
            }
        }

        return new AgendaSummary(overdue, todays, week, certificates);
    }

    /// <summary>Il numero accanto a "Calendario" nella barra laterale: quello di oggi e quello rimasto indietro.</summary>
    public int Badge(DateTime now)
    {
        AgendaSummary summary = Summary(now);
        return summary.Today.Count + summary.Overdue.Count;
    }

    // ------------------------------------------------------------------ cambiamenti

    /// <summary>
    /// Un cambiamento chiesto dalla pagina del calendario (o da un avviso): put (una voce nuova o
    /// cambiata), remove, skip (toglie una volta), done (fatta o no), ignore (un host dei
    /// certificati), certs (impostazioni dei certificati). Torna true se il calendario e' cambiato.
    /// </summary>
    public bool Apply(JsonObject change)
    {
        switch (Text(change["op"]))
        {
            case "put" when change["item"] is JsonObject written && ReadItem(written) is { } item:
            {
                if (Text(written["id"]).StartsWith(CertificatePrefix, StringComparison.Ordinal))
                {
                    return false;
                }

                int at = Items.FindIndex(other => other.Id == item.Id);

                if (at >= 0)
                {
                    Items[at] = item;
                }
                else if (Items.Count < MostItems)
                {
                    Items.Add(item);
                }
                else
                {
                    return false;
                }

                return true;
            }

            case "remove":
                return Items.RemoveAll(other => other.Id == Text(change["id"])) > 0;

            case "skip" when Find(Text(change["id"])) is { Repeat.Length: > 0 } item && Day(Text(change["date"])) is { } day:
                item.Done.Remove(day);
                return item.Skip.Add(day);

            case "done" when Day(Text(change["date"])) is { } day:
                return SetDone(Text(change["id"]), day, Flag(change["on"]));

            case "ignore" when Clean(Text(change["host"]), 253) is { Length: > 0 } host:
                return Flag(change["on"]) ? IgnoredHosts.Add(host) : IgnoredHosts.Remove(host);

            case "certs":
            {
                bool changed = false;

                if (change["off"] is JsonValue off && off.TryGetValue(out bool isOff) && isOff != CertificatesOff)
                {
                    CertificatesOff = isOff;
                    changed = true;
                }

                if (change["remind"] is JsonArray remind && Reminders(remind) is var minutes && !minutes.SequenceEqual(CertificateRemind))
                {
                    CertificateRemind = minutes;
                    changed = true;
                }

                return changed;
            }

            default:
                return false;
        }
    }

    /// <summary>Segna una volta come fatta (o no); per i certificati, come rinnovato.</summary>
    public bool SetDone(string id, DateOnly day, bool on)
    {
        if (id.StartsWith(CertificatePrefix, StringComparison.Ordinal))
        {
            string host = id[CertificatePrefix.Length..];

            if (Certificates.FirstOrDefault(seen => seen.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) is not { } seen)
            {
                return false;
            }

            string key = CertificateKey(seen.Host, day);
            return on ? CertificatesDone.Add(key) : CertificatesDone.Remove(key);
        }

        if (Find(id) is not { } item)
        {
            return false;
        }

        return on ? item.Done.Add(day) : item.Done.Remove(day);
    }

    public AgendaItem? Find(string id) => Items.FirstOrDefault(item => item.Id == id);

    /// <summary>
    /// I certificati visti dalle analisi: uno per host, l'ultimo visto vince (un certificato
    /// rinnovato sposta la scadenza). Torna true se qualcosa e' cambiato.
    /// </summary>
    public bool Learn(IEnumerable<CertificateSeen> sightings)
    {
        bool changed = false;

        foreach (CertificateSeen seen in sightings)
        {
            if (seen.Host.Length == 0)
            {
                continue;
            }

            int at = Certificates.FindIndex(known => known.Host.Equals(seen.Host, StringComparison.OrdinalIgnoreCase));

            if (at >= 0)
            {
                // lo stesso host scritto con altre maiuscole non e' un cambiamento
                if (Certificates[at] != seen with { Host = Certificates[at].Host })
                {
                    Certificates[at] = seen with { Host = Certificates[at].Host };
                    changed = true;
                }
            }
            else if (Certificates.Count < MostCertificates)
            {
                Certificates.Add(seen);
                changed = true;
            }
        }

        return changed;
    }

    public static string CertificateKey(string host, DateOnly day) => host.ToLowerInvariant() + "@" + Iso(day);

    // ------------------------------------------------------------------ attrezzi

    public static string NewId() => "e" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? Day(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) && day.Year is >= 1900 and <= 2200
            ? day
            : null;

    private static TimeOnly? Hour(string text) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time) ? time : null;

    private static List<int> Reminders(JsonArray list) =>
    [
        .. list.Select(node => node is JsonValue value && value.TryGetValue(out int minutes) ? minutes : -1)
            .Where(minutes => minutes is >= 0 and <= LongestReminder)
            .Distinct()
            .OrderByDescending(minutes => minutes)
            .Take(MostReminders)
    ];

    private static AgendaItem Copy(AgendaItem item, string id)
    {
        AgendaItem copy = new()
        {
            Id = id,
            Kind = item.Kind,
            Title = item.Title,
            Date = item.Date,
            Time = item.Time,
            Notes = item.Notes,
            Remind = [.. item.Remind],
            Repeat = item.Repeat,
            Every = item.Every,
            Until = item.Until,
            Link = item.Link
        };
        copy.Done.UnionWith(item.Done);
        copy.Skip.UnionWith(item.Skip);
        return copy;
    }

    private static bool IsId(string id) => id.Length is >= 2 and <= 24 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool on) && on;

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;

    private static void AddText(JsonObject target, string name, string value)
    {
        if (value.Length > 0)
        {
            target[name] = value;
        }
    }

    private static string Clean(string text, int most)
    {
        string clean = new string([.. text.Where(c => !char.IsControl(c))]).Trim();
        return clean.Length > most ? clean[..most] : clean;
    }

    private static string CleanNotes(string text)
    {
        string clean = new string([.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Where(c => c is '\n' or '\t' || !char.IsControl(c))]).Trim();
        return clean.Length > 4000 ? clean[..4000] : clean;
    }
}
