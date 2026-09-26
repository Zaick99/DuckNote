using System.Buffers.Binary;

namespace DuckNote.Core.Vault;

public static class ContentPadding
{
    private const int LengthPrefix = 4;
    private const int SmallStep = 4096;
    private const int LargeStep = 65536;
    private const int LargeThreshold = 1024 * 1024;

    public static int PaddedSize(int usefulLength)
    {
        int step = usefulLength < LargeThreshold ? SmallStep : LargeStep;
        return (int)(Math.Ceiling((usefulLength + (double)LengthPrefix) / step) * step);
    }

    public static byte[] Pad(byte[]? plain)
    {
        plain ??= [];

        byte[] padded = new byte[PaddedSize(plain.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(padded, plain.Length);
        plain.CopyTo(padded, LengthPrefix);
        return padded;
    }

    public static byte[]? Unpad(byte[]? padded)
    {
        if (padded is null || padded.Length < LengthPrefix)
        {
            return null;
        }

        int useful = BinaryPrimitives.ReadInt32LittleEndian(padded);
        if (useful < 0 || useful > padded.Length - LengthPrefix)
        {
            return null;
        }

        return padded.AsSpan(LengthPrefix, useful).ToArray();
    }
}
