using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace DuckNote.Core.Crypto;

public static class PasswordBytes
{
    public static byte[] FromSecureString(SecureString? password)
    {
        if (password is null || password.Length == 0)
        {
            return [];
        }

        IntPtr unmanaged = IntPtr.Zero;
        char[] characters = new char[password.Length];
        try
        {
            unmanaged = Marshal.SecureStringToGlobalAllocUnicode(password);
            for (int i = 0; i < characters.Length; i++)
            {
                characters[i] = (char)Marshal.ReadInt16(unmanaged, i * 2);
            }
            return Encoding.UTF8.GetBytes(characters);
        }
        finally
        {
            Array.Clear(characters);
            if (unmanaged != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanaged);
            }
        }
    }

    public static bool Equal(byte[]? left, byte[]? right) =>
        left is not null && right is not null && CryptographicOperations.FixedTimeEquals(left, right);
}
