using System.Net;

namespace DuckNote.Scan;

public static class TargetList
{
    private const int MaximumHosts = 65536;

    public static IReadOnlyList<string> Expand(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        List<string> targets = [];
        foreach (string piece in expression.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            targets.AddRange(ExpandPiece(piece.Trim()));
            if (targets.Count > MaximumHosts)
            {
                throw new ArgumentException(
                    $"L'intervallo supera {MaximumHosts} host: restringilo.", nameof(expression));
            }
        }

        return [.. targets.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> ExpandPiece(string piece)
    {
        if (piece.Length == 0)
        {
            return [];
        }
        if (piece.Contains('/'))
        {
            return ExpandCidr(piece);
        }
        if (piece.Contains('-'))
        {
            return ExpandRange(piece);
        }
        return [piece];
    }

    private static IEnumerable<string> ExpandCidr(string piece)
    {
        string[] parts = piece.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out IPAddress? network)
            || !int.TryParse(parts[1], out int prefix)
            || network.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || prefix is < 0 or > 32)
        {
            return [piece];
        }

        uint address = ToUInt32(network);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        uint first = address & mask;
        uint last = first | ~mask;

        if (prefix < 31)
        {
            first++;
            last--;
        }

        return Enumerate(first, last);
    }

    private static IEnumerable<string> ExpandRange(string piece)
    {
        string[] ends = piece.Split('-', 2);
        string start = ends[0].Trim();
        string end = ends[1].Trim();

        if (!IPAddress.TryParse(start, out IPAddress? from))
        {
            return [piece];
        }

        if (!end.Contains('.') && int.TryParse(end, out int lastOctet))
        {
            uint baseAddress = ToUInt32(from);
            uint upper = (baseAddress & 0xFFFFFF00u) | (uint)(lastOctet & 0xFF);
            return upper < baseAddress ? [piece] : Enumerate(baseAddress, upper);
        }

        if (!IPAddress.TryParse(end, out IPAddress? to))
        {
            return [piece];
        }

        uint low = ToUInt32(from);
        uint high = ToUInt32(to);
        return low > high ? [piece] : Enumerate(low, high);
    }

    private static IEnumerable<string> Enumerate(uint first, uint last)
    {
        for (uint value = first; value <= last; value++)
        {
            yield return FromUInt32(value);

            if (value == uint.MaxValue)
            {
                yield break;
            }
        }
    }

    private static uint ToUInt32(IPAddress address)
    {
        byte[] octets = address.GetAddressBytes();
        return ((uint)octets[0] << 24) | ((uint)octets[1] << 16) | ((uint)octets[2] << 8) | octets[3];
    }

    private static string FromUInt32(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
