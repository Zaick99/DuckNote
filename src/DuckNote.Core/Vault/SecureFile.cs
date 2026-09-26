using System.Security.Cryptography;

namespace DuckNote.Core.Vault;

public static class SecureFile
{
    private static readonly DateTime Pinned = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const int ChunkSize = 65536;

    public static void PinTimestamps(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetCreationTimeUtc(path, Pinned);
            File.SetLastWriteTimeUtc(path, Pinned);
            File.SetLastAccessTimeUtc(path, Pinned);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Shred(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            long length = new FileInfo(path).Length;
            if (length > 0)
            {
                Overwrite(path, length);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Overwrite(string path, long length)
    {
        using FileStream file = File.OpenWrite(path);
        byte[] noise = RandomNumberGenerator.GetBytes((int)Math.Min(length, ChunkSize));

        for (long written = 0; written < length;)
        {
            int take = (int)Math.Min(noise.Length, length - written);
            file.Write(noise, 0, take);
            written += take;
        }

        file.Flush();
    }

    public static void WriteAtomically(string path, byte[] content)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
        PinTimestamps(path);
    }
}
