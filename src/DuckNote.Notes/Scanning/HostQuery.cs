namespace DuckNote.Notes.Scanning;

/// <summary>
/// Il filtro della tabella degli host. Testo libero cerca dappertutto; "campo:valore" cerca
/// in un campo solo (ip: nome: mac: porta: os: tipo: produttore: gruppo: web: nota:). Piu'
/// condizioni devono valere tutte. Un valore fra virgolette puo' contenere spazi.
/// </summary>
public sealed class HostQuery
{
    public static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ip"] = "ip",
        ["nome"] = "nome",
        ["mac"] = "mac",
        ["porta"] = "porte",
        ["porte"] = "porte",
        ["os"] = "os",
        ["tipo"] = "tipo",
        ["produttore"] = "produttore",
        ["gruppo"] = "gruppo",
        ["web"] = "web",
        ["nota"] = "nota",
        ["stato"] = "stato"
    };

    private readonly List<(string? Field, string Value)> _terms = [];

    public bool IsEmpty => _terms.Count == 0;

    public static HostQuery Parse(string? text)
    {
        HostQuery query = new();

        foreach (string word in Words(text ?? string.Empty))
        {
            int colon = word.IndexOf(':');

            if (colon > 0 && Fields.TryGetValue(word[..colon], out string? field))
            {
                string value = word[(colon + 1)..].Trim('"');
                if (value.Length > 0)
                {
                    query._terms.Add((field, value));
                }

                continue;
            }

            query._terms.Add((null, word.Trim('"')));
        }

        return query;
    }

    /// <summary>
    /// I campi di un host per nome canonico (ip, nome, mac, porte, os, tipo, produttore,
    /// gruppo, web, nota, stato).
    /// </summary>
    public bool Matches(IReadOnlyDictionary<string, string> facts)
    {
        foreach ((string? field, string value) in _terms)
        {
            bool hit = field is null
                ? facts.Values.Any(text => Contains(text, value))
                : facts.TryGetValue(field, out string? text) && (field == "porte" ? Port(text, value) : Contains(text, value));

            if (!hit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"porta:22" vuole la porta 22, non la 2222.</summary>
    private static bool Port(string ports, string wanted) =>
        ports.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Any(port => port == wanted);

    private static bool Contains(string? text, string value) =>
        text is not null && text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Words(string text)
    {
        System.Text.StringBuilder word = new();
        bool quoted = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                word.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }

                continue;
            }

            word.Append(c);
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }
}
