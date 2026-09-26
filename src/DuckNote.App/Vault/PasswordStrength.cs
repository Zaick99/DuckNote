using System.Security;
using System.Security.Cryptography;
using DuckNote.Core.Crypto;

namespace DuckNote.App.Vault;

public static class PasswordStrength
{
    public static int Bits(SecureString? password)
    {
        if (password is null || password.Length == 0)
        {
            return 0;
        }

        byte[] bytes = PasswordBytes.FromSecureString(password);
        try
        {
            bool lower = false, upper = false, digits = false, other = false;

            foreach (byte b in bytes)
            {
                if (b is >= 0x61 and <= 0x7A) { lower = true; }
                else if (b is >= 0x41 and <= 0x5A) { upper = true; }
                else if (b is >= 0x30 and <= 0x39) { digits = true; }
                else { other = true; }
            }

            int alphabet = 0;
            if (lower) { alphabet += 26; }
            if (upper) { alphabet += 26; }
            if (digits) { alphabet += 10; }
            if (other) { alphabet += 33; }

            return (int)(password.Length * Math.Log(Math.Max(2, alphabet), 2));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static (string Token, string Word) Describe(int bits) => bits switch
    {
        0 => ("BorderControl", ""),
        < 40 => ("Red", "fragile"),
        < 60 => ("Yellow", "discreta"),
        < 85 => ("Green", "solida"),
        _ => ("Green", "da manuale")
    };

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
}
