using System.Security.Cryptography;

namespace DuckNote.Core.Crypto;

public static class KeyDerivation
{
    public const int KeyEncryptionKeyLength = 64;

    public static Task<byte[]> DeriveKeyEncryptionKeyAsync(
        byte[] password,
        byte[] salt,
        KdfParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(parameters);

        return Task.Run(() => DeriveKeyEncryptionKey(password, salt, parameters), cancellationToken);
    }

    public static byte[] DeriveKeyEncryptionKey(byte[] password, byte[] salt, KdfParameters parameters)
    {
        byte[]? stretched = null;
        try
        {
            stretched = Argon2id.Hash(
                password, salt, secret: null, associated: null,
                parameters.MemoryKib, parameters.Passes, parameters.Lanes, outputLength: 64);

            return Rfc2898DeriveBytes.Pbkdf2(
                stretched, salt, parameters.Pbkdf2Iterations,
                HashAlgorithmName.SHA512, KeyEncryptionKeyLength);
        }
        finally
        {
            if (stretched is not null)
            {
                CryptographicOperations.ZeroMemory(stretched);
            }
            CryptographicOperations.ZeroMemory(password);
        }
    }

    public static (byte[] Encryption, byte[] Authentication) SplitKeyEncryptionKey(byte[] kek)
    {
        ArgumentNullException.ThrowIfNull(kek);
        if (kek.Length != KeyEncryptionKeyLength)
        {
            throw new ArgumentException($"La KEK deve essere di {KeyEncryptionKeyLength} byte.", nameof(kek));
        }

        return (kek[..32], kek[32..]);
    }
}
