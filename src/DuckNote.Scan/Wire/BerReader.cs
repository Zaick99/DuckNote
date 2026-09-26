using System.Globalization;
using System.Text;

namespace DuckNote.Scan.Wire;

public static class BerReader
{
    private const byte TimeTicks = 0x43;
    private const byte IpAddress = 0x40;

    public readonly record struct Node(byte Tag, int Offset, int Length);

    public static IReadOnlyList<Node> Children(byte[] data, int start, int end)
    {
        if (end < 0 || end > data.Length)
        {
            end = data.Length;
        }

        List<Node> nodes = [];
        int index = start;

        while (index + 2 <= end)
        {
            byte tag = data[index++];
            int length = data[index++];

            if ((length & 0x80) != 0)
            {
                int extra = length & 0x7F;
                if (extra is < 1 or > 4 || index + extra > end)
                {
                    break;
                }

                length = 0;
                for (int k = 0; k < extra; k++)
                {
                    length = (length << 8) | data[index + k];
                }
                index += extra;
            }

            if (length < 0 || index + length > end)
            {
                break;
            }

            nodes.Add(new Node(tag, index, length));
            index += length;
        }

        return nodes;
    }

    public static string ReadValue(byte[] data, Node node) => node.Tag switch
    {
        BerWriter.OctetString => TextSanitiser.Clean(Encoding.UTF8.GetString(data, node.Offset, node.Length), 400),
        BerWriter.Integer => ReadInteger(data, node).ToString(CultureInfo.InvariantCulture),
        TimeTicks => DescribeUptime(ReadInteger(data, node)),
        BerWriter.ObjectIdentifier => ReadOid(data, node),
        IpAddress => string.Join('.', data.Skip(node.Offset).Take(node.Length)),
        _ => string.Empty
    };

    private static long ReadInteger(byte[] data, Node node)
    {
        long value = 0;
        for (int k = 0; k < node.Length; k++)
        {
            value = (value << 8) | data[node.Offset + k];
        }
        return value;
    }

    private static string DescribeUptime(long ticks)
    {
        TimeSpan uptime = TimeSpan.FromSeconds(ticks / 100.0);
        return $"{uptime.Days}g {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    private static string ReadOid(byte[] data, Node node)
    {
        if (node.Length == 0)
        {
            return string.Empty;
        }

        int first = data[node.Offset];
        List<int> arcs = [first / 40, first % 40];

        int accumulator = 0;
        for (int k = 1; k < node.Length; k++)
        {
            byte b = data[node.Offset + k];
            accumulator = (accumulator << 7) | (b & 0x7F);

            if ((b & 0x80) == 0)
            {
                arcs.Add(accumulator);
                accumulator = 0;
            }
        }

        return string.Join('.', arcs);
    }
}
