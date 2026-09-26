using System.Security.Cryptography;
using System.Text;
using DuckNote.Core.Crypto;

namespace DuckNote.Core.Vault;

public static class SealedBlob
{
    private const string PaddedMark = "DND2";
    private const string WrapperMark = "DNW2";
    private const string LegacyPaddedMark = "DND1";

    private const int MarkLength = 4;
    private const int IvLength = 16;
    private const int TagLength = 32;
    private const int MinimumLength = MarkLength + IvLength + 16 + TagLength;

    public static byte[] Seal(byte[]? plain, byte[] encryptionKey, byte[] authenticationKey, bool padded)
    {
        ArgumentNullException.ThrowIfNull(encryptionKey);
        ArgumentNullException.ThrowIfNull(authenticationKey);

        byte[] iv = RandomNumberGenerator.GetBytes(IvLength);
        byte[] content = padded ? ContentPadding.Pad(plain) : plain ?? [];

        byte[] cipherText;
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Key = encryptionKey;
            try
            {
                cipherText = aes.EncryptCbc(content, iv, PaddingMode.PKCS7);
            }
            finally
            {
                if (padded)
                {
                    CryptographicOperations.ZeroMemory(content);
                }
            }
        }

        byte[] mark = Encoding.ASCII.GetBytes(padded ? PaddedMark : WrapperMark);
        byte[] body = new byte[mark.Length + IvLength + cipherText.Length];
        mark.CopyTo(body, 0);
        iv.CopyTo(body, mark.Length);
        cipherText.CopyTo(body, mark.Length + IvLength);

        byte[] tag = HMACSHA256.HashData(authenticationKey, body);

        byte[] blob = new byte[body.Length + TagLength];
        body.CopyTo(blob, 0);
        tag.CopyTo(blob, body.Length);
        return blob;
    }

    public static byte[]? Open(byte[]? blob, byte[] encryptionKey, byte[] authenticationKey)
    {
        ArgumentNullException.ThrowIfNull(encryptionKey);
        ArgumentNullException.ThrowIfNull(authenticationKey);

        if (blob is null || blob.Length < MinimumLength)
        {
            return null;
        }

        string mark = Encoding.ASCII.GetString(blob, 0, MarkLength);
        if (mark is not (PaddedMark or WrapperMark or LegacyPaddedMark))
        {
            return null;
        }

        int bodyLength = blob.Length - TagLength;
        byte[] expected = HMACSHA256.HashData(authenticationKey, blob.AsSpan(0, bodyLength).ToArray());
        if (!PasswordBytes.Equal(expected, blob.AsSpan(bodyLength).ToArray()))
        {
            return null;
        }

        byte[] plain;
        try
        {
            using Aes aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = encryptionKey;
            plain = aes.DecryptCbc(
                blob.AsSpan(MarkLength + IvLength, bodyLength - MarkLength - IvLength),
                blob.AsSpan(MarkLength, IvLength),
                PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            return null;
        }

        return mark == WrapperMark ? plain : ContentPadding.Unpad(plain);
    }
}
