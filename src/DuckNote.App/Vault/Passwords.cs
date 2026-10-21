using System.Security;
using System.Security.Cryptography;
using DuckNote.Core.Crypto;
using DuckNote.Notes.Security;

namespace DuckNote.App.Vault;

/// <summary>Le password lette dalle caselle: misurate e confrontate senza lasciarle in giro come stringhe.</summary>
public static class Passwords
{
    public const int Shortest = 8;

    public static int Bits(SecureString? password)
    {
        if (password is null || password.Length == 0)
        {
            return 0;
        }

        byte[] bytes = PasswordBytes.FromSecureString(password);

        try
        {
            return PasswordStrength.Bits(bytes, password.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static bool Same(SecureString? first, SecureString? second)
    {
        byte[] a = PasswordBytes.FromSecureString(first);
        byte[] b = PasswordBytes.FromSecureString(second);

        try
        {
            return PasswordBytes.Equal(a, b);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(a);
            CryptographicOperations.ZeroMemory(b);
        }
    }

    public static string Cost(KdfParameters kdf) =>
        $"Argon2id {kdf.MemoryKib / 1024} MiB, {kdf.Passes} passate, poi PBKDF2-SHA512 con {kdf.Pbkdf2Iterations:N0} giri";
}
