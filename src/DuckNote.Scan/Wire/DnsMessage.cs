using System.Text;

namespace DuckNote.Scan.Wire;

public static class DnsMessage
{
    public const int TypeA = 1;
    public const int TypePtr = 12;
    public const int TypeTxt = 16;
    public const int TypeSrv = 33;

    private const int HeaderLength = 12;
    private const int MaximumLabels = 64;
    private const int PointerMask = 0xC0;

    public readonly record struct DecodedName(string Name, int Next);

    public static string ArpaName(string address)
    {
        string[] octets = address.Split('.');
        return octets.Length != 4
            ? string.Empty
            : $"{octets[3]}.{octets[2]}.{octets[1]}.{octets[0]}.in-addr.arpa";
    }

    public static byte[] EncodeLabels(string name)
    {
        List<byte> encoded = [];
        foreach (string label in name.Split('.'))
        {
            if (label.Length == 0)
            {
                continue;
            }
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            encoded.Add((byte)bytes.Length);
            encoded.AddRange(bytes);
        }
        encoded.Add(0x00);
        return [.. encoded];
    }

    public static byte[] BuildQuery(string name, int type, ushort transactionId, bool recursionDesired = true)
    {
        List<byte> packet =
        [
            (byte)(transactionId >> 8), (byte)(transactionId & 0xFF),
            recursionDesired ? (byte)0x01 : (byte)0x00, 0x00,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00
        ];

        packet.AddRange(EncodeLabels(name));
        packet.AddRange([(byte)((type >> 8) & 0xFF), (byte)(type & 0xFF), 0x00, 0x01]);
        return [.. packet];
    }

    /// <summary>
    /// Una domanda mDNS con piu' domande insieme, a cui rispondere direttamente a chi chiede
    /// (bit QU): la si manda alla porta 5353 dell'host, che risponde a noi (legacy unicast).
    /// </summary>
    public static byte[] BuildMdnsQuery(IReadOnlyList<(string Name, int Type)> questions, ushort transactionId)
    {
        List<byte> packet =
        [
            (byte)(transactionId >> 8), (byte)(transactionId & 0xFF),
            0x00, 0x00,
            (byte)(questions.Count >> 8), (byte)(questions.Count & 0xFF),
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00
        ];

        foreach ((string name, int type) in questions)
        {
            packet.AddRange(EncodeLabels(name));
            packet.AddRange([(byte)((type >> 8) & 0xFF), (byte)(type & 0xFF), 0x80, 0x01]);
        }

        return [.. packet];
    }

    /// <summary>Un record di una risposta: il nome, il tipo e dove stanno i suoi dati nel pacchetto.</summary>
    public readonly record struct Record(string Name, int Type, int DataOffset, int DataLength);

    /// <summary>Tutti i record di una risposta: risposte, autorita' e aggiuntivi (dove mDNS mette TXT e SRV).</summary>
    public static IReadOnlyList<Record> ReadRecords(byte[] data)
    {
        List<Record> records = [];
        if (data.Length < HeaderLength)
        {
            return records;
        }

        int questions = (data[4] << 8) | data[5];
        int total = ((data[6] << 8) | data[7]) + ((data[8] << 8) | data[9]) + ((data[10] << 8) | data[11]);
        int index = HeaderLength;

        for (int q = 0; q < questions && index < data.Length; q++)
        {
            index = DecodeName(data, index).Next + 4;
        }

        for (int r = 0; r < total && index < data.Length; r++)
        {
            DecodedName name = DecodeName(data, index);
            index = name.Next;
            if (index + 10 > data.Length)
            {
                break;
            }

            int type = (data[index] << 8) | data[index + 1];
            int length = (data[index + 8] << 8) | data[index + 9];
            int offset = index + 10;
            if (offset + length > data.Length)
            {
                break;
            }

            records.Add(new Record(name.Name, type, offset, length));
            index = offset + length;
        }

        return records;
    }

    /// <summary>Le stringhe di un record TXT ("model=MacBookPro18,3", "md=Chromecast").</summary>
    public static IReadOnlyList<string> ReadTxt(byte[] data, int offset, int length)
    {
        List<string> strings = [];
        int end = Math.Min(data.Length, offset + length);
        for (int index = offset; index < end;)
        {
            int size = data[index];
            if (index + 1 + size > end)
            {
                break;
            }
            if (size > 0)
            {
                strings.Add(Encoding.UTF8.GetString(data, index + 1, size));
            }
            index += 1 + size;
        }
        return strings;
    }

    public static DecodedName DecodeName(byte[] data, int offset)
    {
        List<string> labels = [];
        int index = offset;
        bool jumped = false;
        int guard = 0;

        while (index < data.Length && guard < MaximumLabels)
        {
            guard++;
            byte length = data[index];

            if (length == 0)
            {
                index++;
                break;
            }

            if ((length & PointerMask) == PointerMask)
            {
                if (index + 1 >= data.Length)
                {
                    break;
                }

                int pointer = ((length & 0x3F) << 8) | data[index + 1];
                if (!jumped)
                {
                    index += 2;
                }
                jumped = true;

                if (pointer >= data.Length || pointer == offset)
                {
                    break;
                }

                DecodedName referenced = DecodeName(data, pointer);
                if (referenced.Name.Length > 0)
                {
                    labels.Add(referenced.Name);
                }
                break;
            }

            if (index + 1 + length > data.Length)
            {
                break;
            }

            labels.Add(Encoding.UTF8.GetString(data, index + 1, length));
            index += 1 + length;
        }

        return new DecodedName(string.Join('.', labels), index);
    }

    public static string ReadAnswer(byte[] data, int type)
    {
        if (data.Length < HeaderLength)
        {
            return string.Empty;
        }

        int answers = (data[6] << 8) | data[7];
        if (answers <= 0)
        {
            return string.Empty;
        }

        int questions = (data[4] << 8) | data[5];
        int index = HeaderLength;

        for (int q = 0; q < questions; q++)
        {
            index = DecodeName(data, index).Next + 4;
        }

        for (int a = 0; a < answers && index < data.Length; a++)
        {
            index = DecodeName(data, index).Next;
            if (index + 10 > data.Length)
            {
                break;
            }

            int recordType = (data[index] << 8) | data[index + 1];
            int dataLength = (data[index + 8] << 8) | data[index + 9];
            int dataOffset = index + 10;

            if (recordType == type && type == TypePtr)
            {
                string name = DecodeName(data, dataOffset).Name;
                if (name.Length > 0)
                {
                    return name;
                }
            }

            if (recordType == type && type == TypeA && dataLength == 4 && dataOffset + 4 <= data.Length)
            {
                return $"{data[dataOffset]}.{data[dataOffset + 1]}.{data[dataOffset + 2]}.{data[dataOffset + 3]}";
            }

            index = dataOffset + dataLength;
        }

        return string.Empty;
    }
}
