using System.Security.Cryptography;
using System.Text;
using DuckNote.Core.Vault;

namespace DuckNote.Core.Tests;

public class SealedBlobTests
{
    private static readonly byte[] EncryptionKey = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] AuthenticationKey = RandomNumberGenerator.GetBytes(32);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4091)]
    [InlineData(4092)]
    [InlineData(4093)]
    [InlineData(100_000)]
    public void A_sealed_payload_comes_back_byte_for_byte(int length)
    {
        byte[] plain = RandomNumberGenerator.GetBytes(length);

        byte[] blob = SealedBlob.Seal(plain, EncryptionKey, AuthenticationKey, padded: true);

        Assert.Equal(plain, SealedBlob.Open(blob, EncryptionKey, AuthenticationKey));
    }

    [Fact]
    public void Accented_text_survives_the_round_trip()
    {
        byte[] plain = Encoding.UTF8.GetBytes("Però l'anatra è già qui — 192.168.1.1");

        byte[] blob = SealedBlob.Seal(plain, EncryptionKey, AuthenticationKey, padded: true);

        Assert.Equal(plain, SealedBlob.Open(blob, EncryptionKey, AuthenticationKey));
    }

    [Fact]
    public void The_size_on_disk_says_nothing_about_what_was_written()
    {
        byte[] shortNote = SealedBlob.Seal(new byte[10], EncryptionKey, AuthenticationKey, padded: true);
        byte[] longNote = SealedBlob.Seal(new byte[3000], EncryptionKey, AuthenticationKey, padded: true);

        Assert.Equal(shortNote.Length, longNote.Length);
    }

    [Fact]
    public void Every_write_uses_a_fresh_initialisation_vector()
    {
        byte[] plain = "stesso contenuto"u8.ToArray();

        byte[] first = SealedBlob.Seal(plain, EncryptionKey, AuthenticationKey, padded: true);
        byte[] second = SealedBlob.Seal(plain, EncryptionKey, AuthenticationKey, padded: true);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_wrong_authentication_key_opens_nothing()
    {
        byte[] blob = SealedBlob.Seal("segreto"u8.ToArray(), EncryptionKey, AuthenticationKey, padded: true);

        Assert.Null(SealedBlob.Open(blob, EncryptionKey, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void A_flipped_bit_in_the_ciphertext_is_refused_before_decryption()
    {
        byte[] blob = SealedBlob.Seal("segreto"u8.ToArray(), EncryptionKey, AuthenticationKey, padded: true);
        blob[30] ^= 0x01;

        Assert.Null(SealedBlob.Open(blob, EncryptionKey, AuthenticationKey));
    }

    [Fact]
    public void A_truncated_blob_is_refused()
    {
        byte[] blob = SealedBlob.Seal("segreto"u8.ToArray(), EncryptionKey, AuthenticationKey, padded: true);

        Assert.Null(SealedBlob.Open(blob[..40], EncryptionKey, AuthenticationKey));
    }

    [Fact]
    public void The_wrapper_carries_its_own_mark_and_skips_the_padding()
    {
        byte[] dataKey = RandomNumberGenerator.GetBytes(32);

        byte[] blob = SealedBlob.Seal(dataKey, EncryptionKey, AuthenticationKey, padded: false);

        Assert.Equal("DNW2", Encoding.ASCII.GetString(blob, 0, 4));
        Assert.Equal(dataKey, SealedBlob.Open(blob, EncryptionKey, AuthenticationKey));
    }

    [Fact]
    public void Padded_content_carries_the_padded_mark()
    {
        byte[] blob = SealedBlob.Seal("nota"u8.ToArray(), EncryptionKey, AuthenticationKey, padded: true);

        Assert.Equal("DND2", Encoding.ASCII.GetString(blob, 0, 4));
    }
}
