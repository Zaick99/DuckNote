using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuckNote.Notes.Hosts;

namespace DuckNote.Notes.Groups;

/// <summary>
/// Un gruppo di scansione: un nome, i suoi bersagli (reti come 192.168.1.0/24, intervalli come
/// 192.168.1.10-50, IP singoli, nomi) e se il monitoraggio ne controlla gli host. Il gruppo
/// "Nota" (gli indirizzi scritti nei fogli e sulla mappa) non si salva: c'e' sempre.
/// </summary>
public sealed record ScanGroup(string Id, string Name, IReadOnlyList<string> Targets, bool Monitor)
{
    /// <summary>I bersagli come li vuole la scansione: separati da virgole.</summary>
    public string Expression => string.Join(',', Targets);
}

/// <summary>
/// Il monitoraggio dei gruppi che non si scrivono: la Nota (gli indirizzi dei fogli e della
/// mappa, controllati di serie) e la Ricerca (gli host trovati per intervallo fuori da tutti i
/// gruppi, di serie no).
/// </summary>
public sealed record ScanGroupFlags(bool NoteMonitor = true, bool FoundMonitor = false)
{
    public static readonly ScanGroupFlags Default = new();
}

/// <summary>
/// I gruppi di scansione di una nota, come stanno nel file della nota (gruppi.json, cifrato con
/// lei): { "v": 1, "groups": [ { "id", "name", "targets": [...], "monitor" } ], "note": {
/// "monitor" }, "found": { "monitor" } }.
/// </summary>
public static class ScanGroups
{
    /// <summary>L'identificativo del gruppo di serie, gli host della nota.</summary>
    public const string Note = "nota";

    private const int MostGroups = 100;
    private const int MostTargets = 1000;

    public static IReadOnlyList<ScanGroup> Read(byte[]? json)
    {
        if (json is not { Length: > 0 })
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["groups"] is not JsonArray list)
            {
                return [];
            }

            List<ScanGroup> groups = [];
            HashSet<string> ids = new(StringComparer.Ordinal);

            foreach (JsonNode? item in list.Take(MostGroups))
            {
                if (item is not JsonObject group)
                {
                    continue;
                }

                string id = Text(group["id"]);

                if (!IsId(id) || id == Note || !ids.Add(id))
                {
                    id = NewId();
                    ids.Add(id);
                }

                string name = Clean(Text(group["name"]), 60);
                IReadOnlyList<string> targets = group["targets"] is JsonArray written
                    ? Normalize(written.Select(Text))
                    : [];
                bool monitor = group["monitor"] is JsonValue flag && flag.TryGetValue(out bool on) && on;
                groups.Add(new ScanGroup(id, name.Length > 0 ? name : "Gruppo", targets, monitor));
            }

            return groups;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Il monitoraggio della Nota e della Ricerca; di serie se il file non lo dice.</summary>
    public static ScanGroupFlags ReadFlags(byte[]? json)
    {
        if (json is not { Length: > 0 })
        {
            return ScanGroupFlags.Default;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return ScanGroupFlags.Default;
            }

            static bool? Flag(JsonNode? node) =>
                node is JsonObject part && part["monitor"] is JsonValue value && value.TryGetValue(out bool on) ? on : null;

            return new ScanGroupFlags(
                Flag(root["note"]) ?? ScanGroupFlags.Default.NoteMonitor,
                Flag(root["found"]) ?? ScanGroupFlags.Default.FoundMonitor);
        }
        catch (JsonException)
        {
            return ScanGroupFlags.Default;
        }
    }

    /// <summary>I gruppi per il file della nota; null se non ce n'e' nessuno (la nota resta com'era).</summary>
    public static byte[]? Write(IEnumerable<ScanGroup> groups) => Write(groups, ScanGroupFlags.Default);

    /// <summary>
    /// I gruppi e il monitoraggio di Nota e Ricerca per il file della nota; null se non c'e'
    /// nessun gruppo e il monitoraggio e' quello di serie (la nota resta com'era).
    /// </summary>
    public static byte[]? Write(IEnumerable<ScanGroup> groups, ScanGroupFlags flags)
    {
        JsonArray list = [];

        foreach (ScanGroup group in groups.Take(MostGroups))
        {
            list.Add(new JsonObject
            {
                ["id"] = group.Id,
                ["name"] = group.Name,
                ["targets"] = new JsonArray([.. group.Targets.Select(target => (JsonNode)target)]),
                ["monitor"] = group.Monitor
            });
        }

        if (list.Count == 0 && flags == ScanGroupFlags.Default)
        {
            return null;
        }

        JsonObject root = new() { ["v"] = 1, ["groups"] = list };

        if (flags != ScanGroupFlags.Default)
        {
            root["note"] = new JsonObject { ["monitor"] = flags.NoteMonitor };
            root["found"] = new JsonObject { ["monitor"] = flags.FoundMonitor };
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    /// <summary>I bersagli scritti come capita: uno per riga, o separati da virgole, punti e virgola, spazi.</summary>
    public static IReadOnlyList<string> Split(string? text) =>
        Normalize((text ?? string.Empty).Split(['\n', '\r', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static IReadOnlyList<string> Normalize(IEnumerable<string> targets) =>
        [.. targets.Select(target => target.Trim()).Where(target => target.Length is > 0 and <= 253)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MostTargets)];

    /// <summary>
    /// Cosa non va in un bersaglio, in una frase; null se va bene: un IP, una rete IPv4 (/8 o
    /// piu' stretta), un intervallo (192.168.1.10-50 o 192.168.1.10-192.168.2.20), un nome.
    /// </summary>
    public static string? Problem(string target)
    {
        string text = target.Trim();

        if (text.StartsWith('.'))
        {
            return $"\"{text}\": scrivi la rete per intero, per esempio 192.168.1.1-254.";
        }

        if (text.Contains('/'))
        {
            string[] parts = text.Split('/', 2);
            return IPAddress.TryParse(parts[0], out IPAddress? network) && network.AddressFamily == AddressFamily.InterNetwork
                   && int.TryParse(parts[1], out int prefix) && prefix is >= 8 and <= 32
                ? null
                : $"\"{text}\" non e' una rete IPv4 (da /8 a /32), per esempio 192.168.1.0/24.";
        }

        if (text.Contains('-') && IPAddress.TryParse(text.Split('-', 2)[0], out _))
        {
            return TargetMatcher.Range(text) is { } range && range.To >= range.From
                ? null
                : $"\"{text}\" non e' un intervallo: si scrive 192.168.1.10-50 oppure 192.168.1.10-192.168.2.20.";
        }

        return IPAddress.TryParse(text, out _) || HostTokens.Only(text) is not null
            ? null
            : $"\"{text}\" non e' un indirizzo, una rete o un nome.";
    }

    public static string NewId() => "g" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    /// <summary>
    /// Il gruppo con un host in piu' (dalla pagina Rete: "Aggiungi a un gruppo"): l'indirizzo va
    /// in fondo alle righe, se non c'e' gia' scritto. Il gruppo resta lo stesso se c'era.
    /// </summary>
    public static ScanGroup WithTarget(ScanGroup group, string target)
    {
        string clean = target.Trim();

        return clean.Length == 0 || group.Targets.Contains(clean, StringComparer.OrdinalIgnoreCase)
            ? group
            : group with { Targets = Normalize([.. group.Targets, clean]) };
    }

    /// <summary>
    /// Le righe del gruppo che nominano proprio questo host (il suo IP o uno dei suoi nomi): sono
    /// quelle che "Togli dal gruppo" puo' togliere. Un host che ci sta per una rete o un
    /// intervallo non ha righe sue.
    /// </summary>
    public static IReadOnlyList<string> Listed(ScanGroup group, IEnumerable<string> names)
    {
        HashSet<string> known = new(names.Where(name => name.Length > 0), StringComparer.OrdinalIgnoreCase);
        return [.. group.Targets.Where(known.Contains)];
    }

    /// <summary>Il gruppo senza le righe che nominano questo host.</summary>
    public static ScanGroup WithoutHost(ScanGroup group, IEnumerable<string> names)
    {
        HashSet<string> gone = new(Listed(group, names), StringComparer.OrdinalIgnoreCase);
        return gone.Count == 0 ? group : group with { Targets = [.. group.Targets.Where(target => !gone.Contains(target))] };
    }

    private static bool IsId(string id) => id.Length is >= 2 and <= 24 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;

    private static string Clean(string text, int most)
    {
        string clean = new([.. text.Where(c => !char.IsControl(c))]);
        clean = clean.Trim();
        return clean.Length > most ? clean[..most] : clean;
    }
}

/// <summary>
/// Chi sta in un gruppo, senza espandere le reti: un host ci sta se il suo IP cade in una rete o
/// in un intervallo, o se il suo indirizzo o uno dei suoi nomi e' scritto fra i bersagli.
/// </summary>
public sealed class TargetMatcher
{
    private readonly List<(uint From, uint To)> _ranges = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public TargetMatcher(IEnumerable<string> targets)
    {
        List<string> singles = [];

        foreach (string raw in targets)
        {
            string target = raw.Trim();

            if (target.Length == 0)
            {
                continue;
            }

            if (Range(target) is { } range)
            {
                _ranges.Add(range);
                continue;
            }

            if (_names.Add(Normal(target)))
            {
                singles.Add(target);
            }
        }

        Singles = singles;
    }

    /// <summary>Gli IP singoli e i nomi, come sono scritti: con il monitoraggio acceso stanno in tabella anche prima di un'analisi.</summary>
    public IReadOnlyList<string> Singles { get; }

    /// <summary>Quanti indirizzi coprono le reti e gli intervalli.</summary>
    public long Addresses => _ranges.Sum(range => (long)range.To - range.From + 1);

    public bool Matches(string? ip, params string?[] names)
    {
        if (ip is { Length: > 0 })
        {
            if (_names.Contains(Normal(ip)))
            {
                return true;
            }

            if (Number(ip) is { } value && _ranges.Any(range => value >= range.From && value <= range.To))
            {
                return true;
            }
        }

        return names.Any(name => name is { Length: > 0 } && _names.Contains(Normal(name)));
    }

    /// <summary>Una rete o un intervallo IPv4 come coppia di numeri; null se non lo e'.</summary>
    public static (uint From, uint To)? Range(string target)
    {
        if (target.Contains('/'))
        {
            string[] parts = target.Split('/', 2);

            if (Number(parts[0]) is not { } network || !int.TryParse(parts[1], out int prefix) || prefix is < 0 or > 32)
            {
                return null;
            }

            uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
            return (network & mask, (network & mask) | ~mask);
        }

        if (!target.Contains('-'))
        {
            return null;
        }

        string[] ends = target.Split('-', 2);

        if (Number(ends[0].Trim()) is not { } from)
        {
            return null;
        }

        string end = ends[1].Trim();

        if (!end.Contains('.') && int.TryParse(end, out int last) && last is >= 0 and <= 255)
        {
            uint upper = (from & 0xFFFFFF00u) | (uint)last;
            return upper >= from ? (from, upper) : null;
        }

        return Number(end) is { } to && to >= from ? (from, to) : null;
    }

    private static uint? Number(string text) =>
        IPAddress.TryParse(text.Trim(), out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetwork
            ? address.GetAddressBytes() is var b ? ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3] : null
            : null;

    private static string Normal(string text) =>
        IPAddress.TryParse(text.Trim(), out IPAddress? address) ? address.ToString() : text.Trim().TrimEnd('.');
}
