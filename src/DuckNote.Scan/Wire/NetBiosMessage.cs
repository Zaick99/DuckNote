using System.Text;

namespace DuckNote.Scan.Wire;

public static class NetBiosMessage
{
    private const int HeaderLength = 12;
    private const int EntryLength = 18;
    private const int NameLength = 15;
    private const int MinimumReply = 57;

    public sealed record NodeStatus(
        string Name, string Workgroup, string Mac,
        IReadOnlyList<string> Users, IReadOnlyList<string> Services)
    {
        public static readonly NodeStatus Empty = new("", "", "", [], []);
    }

    public static byte[] EncodeName(string name)
    {
        string padded = name.ToUpperInvariant().PadRight(NameLength)[..NameLength];
        byte[] ascii = Encoding.ASCII.GetBytes(padded);

        List<byte> encoded = new(ascii.Length * 2 + 2);
        foreach (byte b in ascii)
        {
            encoded.Add((byte)(0x41 + ((b >> 4) & 0x0F)));
            encoded.Add((byte)(0x41 + (b & 0x0F)));
        }

        encoded.Add(0x41);
        encoded.Add(0x41);
        return [.. encoded];
    }

    public static byte[] BuildNodeStatusQuery(ushort transactionId)
    {
        List<byte> packet =
        [
            (byte)(transactionId >> 8), (byte)(transactionId & 0xFF),
            0x00, 0x00,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
            0x20
        ];

        packet.AddRange(EncodeName("*"));
        packet.Add(0x00);
        packet.AddRange([0x00, 0x21, 0x00, 0x01]);
        return [.. packet];
    }

    public static NodeStatus ParseNodeStatus(byte[] data)
    {
        if (data.Length < MinimumReply)
        {
            return NodeStatus.Empty;
        }

        int index = HeaderLength;
        while (index < data.Length && data[index] != 0)
        {
            index += data[index] + 1;
        }

        index += 1 + 2 + 2 + 4 + 2;
        if (index >= data.Length)
        {
            return NodeStatus.Empty;
        }

        int count = data[index];
        index++;

        string name = string.Empty;
        string workgroup = string.Empty;
        List<string> users = [];
        List<string> services = [];

        for (int entry = 0; entry < count && index + 17 <= data.Length; entry++, index += EntryLength)
        {
            string entryName = Encoding.ASCII.GetString(data, index, NameLength).Trim();
            byte suffix = data[index + 15];
            int flags = (data[index + 16] << 8) | data[index + 17];
            bool isGroup = (flags & 0x8000) != 0;

            if (isGroup)
            {
                if (workgroup.Length == 0 && suffix is 0x00 or 0x1E)
                {
                    workgroup = entryName;
                }
                continue;
            }

            switch (suffix)
            {
                case 0x00 when name.Length == 0:
                    name = entryName;
                    break;
                case 0x20:
                    services.Add("File server");
                    if (name.Length == 0)
                    {
                        name = entryName;
                    }
                    break;
                case 0x03 when entryName != name:
                    users.Add(entryName);
                    break;
                case 0x1B:
                    services.Add("Domain master browser");
                    break;
                case 0x1D:
                    services.Add("Master browser");
                    break;
                default:
                    break;
            }
        }

        string mac = string.Empty;
        if (index + 6 <= data.Length)
        {
            string candidate = string.Join(':', data.Skip(index).Take(6).Select(b => b.ToString("X2")));
            if (candidate != "00:00:00:00:00:00")
            {
                mac = candidate;
            }
        }

        return new NodeStatus(name, workgroup, mac, users, services);
    }
}
