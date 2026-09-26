namespace DuckNote.Scan.Wire;

public static class BerWriter
{
    public const byte Integer = 0x02;
    public const byte OctetString = 0x04;
    public const byte Null = 0x05;
    public const byte ObjectIdentifier = 0x06;
    public const byte Sequence = 0x30;
    public const byte GetRequest = 0xA0;
    public const byte GetResponse = 0xA2;

    public static byte[] EncodeLength(int length)
    {
        if (length < 128)
        {
            return [(byte)length];
        }

        List<byte> significant = [];
        for (int value = length; value > 0; value >>= 8)
        {
            significant.Insert(0, (byte)(value & 0xFF));
        }

        List<byte> encoded = [(byte)(0x80 | significant.Count)];
        encoded.AddRange(significant);
        return [.. encoded];
    }

    public static byte[] Tlv(byte tag, ReadOnlySpan<byte> value)
    {
        byte[] length = EncodeLength(value.Length);
        byte[] encoded = new byte[1 + length.Length + value.Length];

        encoded[0] = tag;
        length.CopyTo(encoded.AsSpan(1));
        value.CopyTo(encoded.AsSpan(1 + length.Length));
        return encoded;
    }

    public static byte[] EncodeOid(ReadOnlySpan<int> arcs)
    {
        if (arcs.Length < 2)
        {
            throw new ArgumentException("Un OID ha almeno due archi.", nameof(arcs));
        }

        List<byte> encoded = [(byte)((40 * arcs[0]) + arcs[1])];

        for (int i = 2; i < arcs.Length; i++)
        {
            int arc = arcs[i];
            if (arc < 128)
            {
                encoded.Add((byte)arc);
                continue;
            }

            List<byte> chunk = [(byte)(arc & 0x7F)];
            for (int rest = arc >> 7; rest > 0; rest >>= 7)
            {
                chunk.Insert(0, (byte)((rest & 0x7F) | 0x80));
            }
            encoded.AddRange(chunk);
        }

        return [.. encoded];
    }

    public static byte[] EncodeUnsignedInteger(int value)
    {
        List<byte> significant = [];
        for (int rest = value; rest > 0; rest >>= 8)
        {
            significant.Insert(0, (byte)(rest & 0xFF));
        }

        if (significant.Count == 0)
        {
            significant.Add(0);
        }
        if ((significant[0] & 0x80) != 0)
        {
            significant.Insert(0, 0);
        }

        return [.. significant];
    }
}
