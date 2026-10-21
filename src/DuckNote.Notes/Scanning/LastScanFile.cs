using System.Text;
using System.Text.Json;

namespace DuckNote.Notes.Scanning;

/// <summary>
/// L'ultima scansione, per ritrovare gli stati alla riapertura: un elenco di host, ognuno
/// un dizionario campo-valore. In chiaro in lastscan.json, oppure dentro la cassaforte.
/// </summary>
public static class LastScanFile
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = false };

    public static string Write(IEnumerable<IReadOnlyDictionary<string, string>> hosts) =>
        JsonSerializer.Serialize(hosts.Select(host => host.Where(kv => kv.Value.Length > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value)), Format);

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)?
                .Where(host => host.ContainsKey("IP"))
                .Select(host => (IReadOnlyDictionary<string, string>)host)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);
}
