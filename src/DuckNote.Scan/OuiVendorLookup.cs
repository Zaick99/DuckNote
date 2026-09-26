using System.Reflection;
using System.Text.RegularExpressions;

namespace DuckNote.Scan;

public sealed partial class OuiVendorLookup : IVendorLookup
{
    private const string BuiltInResource = "DuckNote.Scan.Resources.oui-builtin.txt";

    private static readonly int[] PrefixLengths = [9, 7, 6];

    private readonly Dictionary<string, string> _vendors;

    public OuiVendorLookup(string? ieeeRegistryPath = null)
    {
        _vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LoadBuiltIn();

        if (ieeeRegistryPath is not null && File.Exists(ieeeRegistryPath))
        {
            LoadIeeeRegistry(ieeeRegistryPath);
        }
    }

    public int Count => _vendors.Count;

    public string Describe(string mac)
    {
        if (string.IsNullOrEmpty(mac))
        {
            return string.Empty;
        }

        string digits = NonHex().Replace(mac, string.Empty).ToUpperInvariant();
        if (digits.Length < 6)
        {
            return string.Empty;
        }

        foreach (int length in PrefixLengths)
        {
            if (digits.Length >= length && _vendors.TryGetValue(digits[..length], out string? vendor))
            {
                return vendor;
            }
        }

        return DescribeUnassigned(digits);
    }

    private static string DescribeUnassigned(string digits)
    {
        if (!byte.TryParse(digits.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte first))
        {
            return string.Empty;
        }
        if ((first & 0x01) != 0)
        {
            return "Indirizzo multicast";
        }
        if ((first & 0x02) != 0)
        {
            return "MAC locale / randomizzato";
        }
        return string.Empty;
    }

    private void LoadBuiltIn()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInResource);
        if (stream is null)
        {
            return;
        }

        using StreamReader reader = new(stream);
        while (reader.ReadLine() is { } line)
        {
            foreach (string entry in line.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                Remember(entry, overwrite: false);
            }
        }
    }

    private void Remember(string entry, bool overwrite)
    {
        string[] halves = entry.Split('=');
        if (halves.Length != 2)
        {
            return;
        }

        string prefix = NonHex().Replace(halves[0], string.Empty).ToUpperInvariant();
        if (prefix.Length is not (6 or 7 or 9))
        {
            return;
        }

        if (overwrite || !_vendors.ContainsKey(prefix))
        {
            _vendors[prefix] = halves[1].Trim();
        }
    }

    private void LoadIeeeRegistry(string path)
    {
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                Match csv = IeeeCsvRow().Match(line);
                if (csv.Success)
                {
                    _vendors[csv.Groups[2].Value.ToUpperInvariant()] =
                        LegalSuffix().Replace(csv.Groups[3].Value.Trim(), string.Empty);
                    continue;
                }

                Match loose = LooseRow().Match(line);
                if (loose.Success)
                {
                    _vendors[loose.Groups[1].Value.ToUpperInvariant()] = loose.Groups[2].Value.Trim();
                }
            }
        }
        catch (IOException)
        {
        }
    }

    [GeneratedRegex("[^0-9A-Fa-f]")]
    private static partial Regex NonHex();

    [GeneratedRegex("""^\s*"?(MA-[LMS])"?\s*,\s*"?([0-9A-Fa-f]{6,9})"?\s*,\s*"?([^",]+)""")]
    private static partial Regex IeeeCsvRow();

    [GeneratedRegex(@"^\s*([0-9A-Fa-f]{6,9})[\s\t;,=|-]+(.+)$")]
    private static partial Regex LooseRow();

    [GeneratedRegex(@"\s*(,?\s*(Inc|Ltd|LLC|GmbH|Co|Corp|Corporation|Limited|S\.?A\.?|B\.?V\.?|Technologies|Technology|Electronics)\.?)+$")]
    private static partial Regex LegalSuffix();
}
