using System.Text.Json;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;

namespace DuckNote.Core.Tests;

public class PowerShellCompatibilityTests
{
    private sealed record Fixture(
        string Password, string Salt, string Kek, string Nota, string Dek,
        int Memory, int Passes, int Lanes, int Iterations);

    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShell");

    private static Fixture Expected { get; } =
        JsonSerializer.Deserialize<Fixture>(
            File.ReadAllText(Path.Combine(FixtureDirectory, "atteso.json")))
        ?? throw new InvalidOperationException("Fixture illeggibile.");

    private static KdfParameters Parameters =>
        new(Expected.Memory, Expected.Passes, Expected.Lanes, Expected.Iterations);

    private static byte[] DeriveKek() =>
        KeyDerivation.DeriveKeyEncryptionKey(
            System.Text.Encoding.UTF8.GetBytes(Expected.Password),
            Convert.FromBase64String(Expected.Salt),
            Parameters);

    [Fact]
    public void The_same_password_yields_the_same_key_encryption_key()
    {
        Assert.Equal(Convert.FromBase64String(Expected.Kek), DeriveKek());
    }

    [Fact]
    public void A_note_sealed_by_PowerShell_opens_here_with_its_accents_intact()
    {
        (byte[] encryption, byte[] authentication) = KeyDerivation.SplitKeyEncryptionKey(DeriveKek());
        byte[] blob = File.ReadAllBytes(Path.Combine(FixtureDirectory, "nota.blob"));

        byte[]? opened = SealedBlob.Open(blob, encryption, authentication);

        Assert.Equal(Convert.FromBase64String(Expected.Nota), opened);
    }

    [Fact]
    public void A_wrapped_data_key_written_by_PowerShell_unwraps_here()
    {
        (byte[] encryption, byte[] authentication) = KeyDerivation.SplitKeyEncryptionKey(DeriveKek());
        byte[] blob = File.ReadAllBytes(Path.Combine(FixtureDirectory, "dek.blob"));

        byte[]? opened = SealedBlob.Open(blob, encryption, authentication);

        Assert.Equal(Convert.FromBase64String(Expected.Dek), opened);
    }

    [Fact]
    public void The_padding_scheme_agrees_on_the_size_PowerShell_chose()
    {
        byte[] blob = File.ReadAllBytes(Path.Combine(FixtureDirectory, "nota.blob"));
        int note = Convert.FromBase64String(Expected.Nota).Length;

        int expectedCipher = ContentPadding.PaddedSize(note) + 16;
        Assert.Equal(4 + 16 + expectedCipher + 32, blob.Length);
    }

    [Fact]
    public void A_note_sealed_here_would_be_opened_by_the_same_keys()
    {
        (byte[] encryption, byte[] authentication) = KeyDerivation.SplitKeyEncryptionKey(DeriveKek());
        byte[] note = Convert.FromBase64String(Expected.Nota);

        byte[] resealed = SealedBlob.Seal(note, encryption, authentication, padded: true);

        Assert.Equal(File.ReadAllBytes(Path.Combine(FixtureDirectory, "nota.blob")).Length, resealed.Length);
        Assert.Equal(note, SealedBlob.Open(resealed, encryption, authentication));
    }
}
