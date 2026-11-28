using DuckNote.Scan.Wire;

namespace DuckNote.Scan.Probes;

/// <summary>
/// Quello che un host annuncia con mDNS: i tipi di servizio (senza "._tcp.local") e i valori dei
/// TXT che servono a riconoscerlo (model, am, md, fn, ty, product, osxvers), il primo che arriva.
/// </summary>
public sealed class MdnsServices
{
    /// <summary>I servizi che si chiedono: quelli che dicono che apparato e'.</summary>
    public static readonly string[] Types =
    [
        "_device-info._tcp.local", "_airplay._tcp.local", "_raop._tcp.local", "_companion-link._tcp.local",
        "_apple-mobdev2._tcp.local", "_rdlink._tcp.local", "_sleep-proxy._udp.local", "_googlecast._tcp.local",
        "_androidtvremote2._tcp.local", "_ipp._tcp.local", "_printer._tcp.local", "_pdl-datastream._tcp.local",
        "_hap._tcp.local", "_smb._tcp.local", "_workstation._tcp.local", "_ssh._tcp.local", "_spotify-connect._tcp.local",
    ];

    /// <summary>Le chiavi dei TXT che si tengono.</summary>
    private static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "am", "md", "fn", "ty", "product", "osxvers", "usb_MFG", "usb_MDL", "mf", "manufacturer",
    };

    public static readonly MdnsServices Empty = new([], new Dictionary<string, string>(), []);

    private readonly HashSet<string> _withTxt;

    private MdnsServices(IReadOnlyCollection<string> services, IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> instances, IEnumerable<string>? withTxt = null)
    {
        Services = services;
        Values = values;
        Instances = instances;
        _withTxt = new HashSet<string>(withTxt ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>I servizi annunciati, brevi: "device-info", "airplay", "googlecast".</summary>
    public IReadOnlyCollection<string> Services { get; }

    /// <summary>I valori dei TXT che contano (chiave in minuscolo).</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>Le istanze dei servizi ("MacBook di Marta._device-info._tcp.local").</summary>
    public IReadOnlyCollection<string> Instances { get; }

    public bool IsEmpty => Services.Count == 0 && Values.Count == 0;

    public bool Has(string service) => Services.Contains(service, StringComparer.OrdinalIgnoreCase);

    public string Value(string key) => Values.TryGetValue(key.ToLowerInvariant(), out string? value) ? value : string.Empty;

    public bool HasTxt(string instance) => _withTxt.Contains(instance);

    /// <summary>I servizi in una riga, per la scheda dell'host.</summary>
    public string Describe() => string.Join(", ", Services.Order(StringComparer.Ordinal));

    /// <summary>Legge una risposta: i PTR dei servizi chiesti e i TXT delle loro istanze.</summary>
    public static MdnsServices Read(byte[] reply)
    {
        IReadOnlyList<DnsMessage.Record> records = DnsMessage.ReadRecords(reply);
        HashSet<string> services = new(StringComparer.OrdinalIgnoreCase);
        List<string> instances = [];
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        List<string> withTxt = [];

        foreach (DnsMessage.Record record in records)
        {
            string name = record.Name.TrimEnd('.');
            if (record.Type == DnsMessage.TypePtr && Array.Exists(Types, type => type.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                services.Add(Short(name));
                string instance = DnsMessage.DecodeName(reply, record.DataOffset).Name.TrimEnd('.');
                if (instance.Length > 0 && !instances.Contains(instance, StringComparer.OrdinalIgnoreCase))
                {
                    instances.Add(instance);
                }
            }
            else if (record.Type == DnsMessage.TypeTxt)
            {
                withTxt.Add(name);
                string service = ServiceOf(name);
                if (service.Length > 0)
                {
                    services.Add(service);
                }

                foreach (string pair in DnsMessage.ReadTxt(reply, record.DataOffset, record.DataLength))
                {
                    int equals = pair.IndexOf('=');
                    if (equals <= 0)
                    {
                        continue;
                    }

                    string key = pair[..equals].Trim().ToLowerInvariant();
                    string value = TextSanitiser.Clean(pair[(equals + 1)..], 80);
                    if (Keys.Contains(key) && value.Length > 0)
                    {
                        values.TryAdd(key, value);
                    }
                }
            }
        }

        return new MdnsServices([.. services], values, instances, withTxt);
    }

    /// <summary>Questo e un'altra risposta insieme.</summary>
    public MdnsServices With(MdnsServices other)
    {
        Dictionary<string, string> values = new(Values, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in other.Values)
        {
            values.TryAdd(key, value);
        }

        return new MdnsServices(
            [.. Services.Union(other.Services, StringComparer.OrdinalIgnoreCase)],
            values,
            [.. Instances.Union(other.Instances, StringComparer.OrdinalIgnoreCase)],
            _withTxt.Union(other._withTxt, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>"_airplay._tcp.local" diventa "airplay".</summary>
    private static string Short(string type)
    {
        string first = type.Split('.')[0];
        return first.TrimStart('_');
    }

    /// <summary>Il servizio di un'istanza ("Salotto._googlecast._tcp.local" e' googlecast), se e' uno di quelli chiesti.</summary>
    private static string ServiceOf(string instance)
    {
        foreach (string type in Types)
        {
            if (instance.EndsWith("." + type, StringComparison.OrdinalIgnoreCase))
            {
                return Short(type);
            }
        }

        return string.Empty;
    }
}
