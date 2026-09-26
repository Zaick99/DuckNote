using System.Security.Cryptography;
using DuckNote.Core.Crypto;

namespace DuckNote.Core.Tests;

public class Argon2idTests
{
    [Fact]
    public void Hash_matches_the_RFC_9106_test_vector()
    {
        byte[] password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        byte[] salt = Enumerable.Repeat((byte)0x02, 16).ToArray();
        byte[] secret = Enumerable.Repeat((byte)0x03, 8).ToArray();
        byte[] associated = Enumerable.Repeat((byte)0x04, 12).ToArray();

        byte[] tag = Argon2id.Hash(
            password, salt, secret, associated,
            memoryKib: 32, iterations: 3, lanes: 4, outputLength: 32);

        Assert.Equal(
            "0D640DF58D78766C08C037A34A8B53C9D01EF0452D75B65EB52520E96B01E659",
            Convert.ToHexString(tag));
    }

    [Fact]
    public void Hash_changes_when_the_salt_changes()
    {
        byte[] password = "anatra"u8.ToArray();
        byte[] first = Argon2id.Hash(password, Enumerable.Repeat((byte)0x0A, 16).ToArray(),
            null, null, memoryKib: 32, iterations: 1, lanes: 1, outputLength: 32);
        byte[] second = Argon2id.Hash(password, Enumerable.Repeat((byte)0x0B, 16).ToArray(),
            null, null, memoryKib: 32, iterations: 1, lanes: 1, outputLength: 32);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_refuses_a_salt_shorter_than_eight_bytes() =>
        Assert.Throws<ArgumentException>(() => Argon2id.Hash(
            [], new byte[4], null, null,
            memoryKib: 32, iterations: 1, lanes: 1, outputLength: 32));

    [Fact]
    public void Derivation_ends_in_a_standard_PBKDF2()
    {
        byte[] salt = Enumerable.Repeat((byte)0x07, 16).ToArray();
        KdfParameters parameters = new(MemoryKib: 32, Passes: 1, Lanes: 1, Pbkdf2Iterations: 1000);

        byte[] stretched = Argon2id.Hash("anatra"u8.ToArray(), salt, null, null,
            parameters.MemoryKib, parameters.Passes, parameters.Lanes, outputLength: 64);
        byte[] expected = Rfc2898DeriveBytes.Pbkdf2(
            stretched, salt, parameters.Pbkdf2Iterations, HashAlgorithmName.SHA512, 64);

        byte[] actual = KeyDerivation.DeriveKeyEncryptionKey("anatra"u8.ToArray(), salt, parameters);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Derivation_wipes_the_password_it_was_given()
    {
        byte[] password = "anatra"u8.ToArray();
        KdfParameters parameters = new(MemoryKib: 32, Passes: 1, Lanes: 1, Pbkdf2Iterations: 10);

        KeyDerivation.DeriveKeyEncryptionKey(password, new byte[16], parameters);

        Assert.All(password, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Splitting_the_key_encryption_key_gives_two_disjoint_halves()
    {
        byte[] kek = RandomNumberGenerator.GetBytes(64);

        (byte[] encryption, byte[] authentication) = KeyDerivation.SplitKeyEncryptionKey(kek);

        Assert.Equal(32, encryption.Length);
        Assert.Equal(32, authentication.Length);
        Assert.Equal(kek[..32], encryption);
        Assert.Equal(kek[32..], authentication);
    }
}
