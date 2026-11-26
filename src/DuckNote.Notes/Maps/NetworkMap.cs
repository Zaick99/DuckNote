using System.Text.Json;
using DuckNote.Notes.Hosts;

namespace DuckNote.Notes.Maps;

/// <summary>
/// Le mappe della rete di una nota, come le scrive la pagina della mappa (mappa.json nel file
/// della nota, cifrato con lei): { "v": 1, "current": id, "maps": [ { "id", "name", "nodes",
/// "links", "zones", "view" } ] }. Qui non si disegna niente: si controlla che quello che arriva
/// dalla pagina sia una mappa, se ne leggono gli indirizzi (DuckNote li controlla come quelli
/// scritti nei fogli) e si contano gli apparati.
/// </summary>
public static class NetworkMap
{
    /// <summary>Oltre questa misura non e' una mappa disegnata a mano: non si tiene.</summary>
    public const int MaxBytes = 16 * 1024 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 16 };

    /// <summary>
    /// Il JSON della pagina, se e' davvero un documento di mappe: compatto, in UTF-8. Null se non
    /// lo e' (testo rovinato, un oggetto senza "maps", troppo grande).
    /// </summary>
    public static byte[]? Clean(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, Options);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("maps", out JsonElement maps)
                || maps.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            using MemoryStream stream = new();

            using (Utf8JsonWriter writer = new(stream))
            {
                root.WriteTo(writer);
            }

            return stream.ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Una sola mappa, senza apparati ne' zone: e' quella che la pagina mostra a una nota senza
    /// mappe, e non vale la pena di scriverla nel file.
    /// </summary>
    public static bool IsBlank(byte[]? json)
    {
        int maps = 0;
        bool empty = true;

        Visit(json, map =>
        {
            maps++;
            empty &= Count(map, "nodes") == 0 && Count(map, "zones") == 0;
        });

        return maps <= 1 && empty;
    }

    /// <summary>
    /// Gli indirizzi degli apparati di tutte le mappe, uno per tipo, nell'ordine in cui stanno:
    /// solo quelli che sono davvero un indirizzo (un IP o un nome di rete), non le parole qualsiasi.
    /// </summary>
    public static IReadOnlyList<string> Hosts(byte[]? json)
    {
        List<string> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        Visit(json, map =>
        {
            if (!map.TryGetProperty("nodes", out JsonElement nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement node in nodes.EnumerateArray())
            {
                if (node.ValueKind == JsonValueKind.Object
                    && node.TryGetProperty("host", out JsonElement host)
                    && host.ValueKind == JsonValueKind.String
                    && HostTokens.Only(host.GetString()) is { } value
                    && seen.Add(value))
                {
                    hosts.Add(value);
                }
            }
        });

        return hosts;
    }

    /// <summary>Quanti apparati ci sono in tutte le mappe.</summary>
    public static int Devices(byte[]? json)
    {
        int devices = 0;
        Visit(json, map => devices += Count(map, "nodes"));
        return devices;
    }

    private static int Count(JsonElement map, string name) =>
        map.TryGetProperty(name, out JsonElement items) && items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;

    private static void Visit(byte[]? json, Action<JsonElement> each)
    {
        if (json is not { Length: > 0 } || json.Length > MaxBytes)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, Options);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("maps", out JsonElement maps)
                && maps.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement map in maps.EnumerateArray())
                {
                    if (map.ValueKind == JsonValueKind.Object)
                    {
                        each(map);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // una mappa che non si legge non ha indirizzi; la pagina la ripulisce quando la apre
        }
    }
}
