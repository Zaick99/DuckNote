namespace DuckNote.Core.Vault;

public sealed class VaultSections
{
    private const int MaximumSections = 32;

    private readonly SortedDictionary<byte, byte[]> _sections = [];

    public int Count => _sections.Count;

    public IReadOnlyCollection<byte> Ids => _sections.Keys;

    public byte[]? Get(VaultSectionId id) =>
        _sections.TryGetValue((byte)id, out byte[]? content) ? [.. content] : null;

    public void Set(VaultSectionId id, byte[]? content)
    {
        byte key = (byte)id;

        if (_sections.TryGetValue(key, out byte[]? previous))
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(previous);
        }

        if (content is null || content.Length == 0)
        {
            _sections.Remove(key);
            return;
        }

        _sections[key] = [.. content];
    }

    public void Clear()
    {
        foreach (byte[] content in _sections.Values)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(content);
        }
        _sections.Clear();
    }

    public byte[] Serialise()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(_sections.Count);
        foreach ((byte id, byte[] content) in _sections)
        {
            writer.Write(id);
            writer.Write(content.Length);
            writer.Write(content);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static VaultSections? Parse(byte[] plain)
    {
        using MemoryStream stream = new(plain, writable: false);
        using BinaryReader reader = new(stream);

        try
        {
            int count = reader.ReadInt32();
            if (count is < 0 or > MaximumSections)
            {
                return null;
            }

            VaultSections sections = new();
            for (int i = 0; i < count; i++)
            {
                byte id = reader.ReadByte();
                int length = reader.ReadInt32();

                if (length < 0 || length > stream.Length - stream.Position)
                {
                    return null;
                }
                sections._sections[id] = reader.ReadBytes(length);
            }

            return sections;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }
}
