using System.Text;
using DuckNote.Core.Crypto;

namespace DuckNote.Core.Vault;

public sealed record VaultHeader(KdfParameters Kdf, byte[] Salt, byte[] Wrapped, byte[] Payload)
{
    private const string Magic = "DNVAULT2";
    private const byte Version = 2;
    private const int MinimumFile = 80;

    private const int MinimumSalt = 8;
    private const int MaximumSalt = 64;
    private const int MinimumWrapped = 68;
    private const int MaximumWrapped = 4096;

    public byte[] Serialise()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);
        writer.Write(Kdf.MemoryKib);
        writer.Write(Kdf.Passes);
        writer.Write((byte)Kdf.Lanes);
        writer.Write(Kdf.Pbkdf2Iterations);
        writer.Write((byte)Salt.Length);
        writer.Write(Salt);
        writer.Write(Wrapped.Length);
        writer.Write(Wrapped);
        writer.Write(Payload.Length);
        writer.Write(Payload);

        writer.Flush();
        return stream.ToArray();
    }

    public static VaultHeader? Parse(byte[] raw)
    {
        if (raw.Length < MinimumFile || Encoding.ASCII.GetString(raw, 0, Magic.Length) != Magic)
        {
            return null;
        }

        using MemoryStream stream = new(raw, writable: false);
        using BinaryReader reader = new(stream);

        try
        {
            reader.ReadBytes(Magic.Length);
            if (reader.ReadByte() != Version)
            {
                return null;
            }

            KdfParameters kdf = new(
                MemoryKib: reader.ReadInt32(),
                Passes: reader.ReadInt32(),
                Lanes: reader.ReadByte(),
                Pbkdf2Iterations: reader.ReadInt32());

            if (!kdf.IsPlausible)
            {
                return null;
            }

            byte saltLength = reader.ReadByte();
            if (saltLength is < MinimumSalt or > MaximumSalt)
            {
                return null;
            }
            byte[] salt = reader.ReadBytes(saltLength);

            int wrappedLength = reader.ReadInt32();
            if (wrappedLength is < MinimumWrapped or > MaximumWrapped)
            {
                return null;
            }
            byte[] wrapped = reader.ReadBytes(wrappedLength);

            int payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || payloadLength > stream.Length - stream.Position)
            {
                return null;
            }

            return new VaultHeader(kdf, salt, wrapped, reader.ReadBytes(payloadLength));
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }
}
