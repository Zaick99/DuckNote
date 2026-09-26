using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;

namespace DuckNote.Core.Tests;

public sealed class VaultStoreTests : IDisposable
{
    private sealed record Fixture(
        string Password, string Salt, string Kek,
        string Nota, string NotaPrec, string Scansione, string Privati,
        int Memory, int Passes, int Lanes, int Iterations);

    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShell");

    private static readonly Fixture Expected =
        JsonSerializer.Deserialize<Fixture>(
            File.ReadAllText(Path.Combine(FixtureDirectory, "vault-atteso.json")))
        ?? throw new InvalidOperationException("Fixture illeggibile.");

    private static KdfParameters Parameters =>
        new(Expected.Memory, Expected.Passes, Expected.Lanes, Expected.Iterations);

    private static byte[] Kek() => Convert.FromBase64String(Expected.Kek);

    private static byte[] Content(string base64) => Convert.FromBase64String(base64);

    private readonly string _workspace =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ducknote-" + Guid.NewGuid().ToString("N"))).FullName;

    private string Scratch(string name) => Path.Combine(_workspace, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_password_still_derives_the_key_that_opens_the_container()
    {
        byte[] derived = KeyDerivation.DeriveKeyEncryptionKey(
            Encoding.UTF8.GetBytes(Expected.Password),
            Convert.FromBase64String(Expected.Salt),
            Parameters);

        Assert.Equal(Kek(), derived);
    }

    [Fact]
    public void A_container_written_by_PowerShell_gives_back_every_section()
    {
        using VaultStore? store = VaultStore.Open(Path.Combine(FixtureDirectory, "store.bin"), Kek());

        Assert.NotNull(store);
        Assert.False(store.IsDamaged);
        Assert.Equal(Content(Expected.Nota), store.GetSection(VaultSectionId.Note));
        Assert.Equal(Content(Expected.NotaPrec), store.GetSection(VaultSectionId.PreviousNote));
        Assert.Equal(Content(Expected.Scansione), store.GetSection(VaultSectionId.LastScan));
        Assert.Equal(Content(Expected.Privati), store.GetSection(VaultSectionId.Private));
    }

    [Fact]
    public void The_note_comes_back_with_its_accents_intact()
    {
        using VaultStore? store = VaultStore.Open(Path.Combine(FixtureDirectory, "store.bin"), Kek());

        string note = Encoding.UTF8.GetString(store!.GetSection(VaultSectionId.Note)!);

        Assert.Contains("però", note, StringComparison.Ordinal);
        Assert.Contains("nas.casa.lan", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_that_was_never_written_is_absent_not_empty()
    {
        using VaultStore? store = VaultStore.Open(Path.Combine(FixtureDirectory, "solo-nota.bin"), Kek());

        Assert.NotNull(store);
        Assert.Equal(Content(Expected.Nota), store.GetSection(VaultSectionId.Note));
        Assert.Null(store.GetSection(VaultSectionId.PreviousNote));
        Assert.Null(store.GetSection(VaultSectionId.LastScan));
        Assert.Null(store.GetSection(VaultSectionId.Private));
    }

    [Fact]
    public void The_file_size_says_nothing_about_what_was_written()
    {
        long full = new FileInfo(Path.Combine(FixtureDirectory, "store.bin")).Length;
        long sparse = new FileInfo(Path.Combine(FixtureDirectory, "solo-nota.bin")).Length;

        Assert.Equal(full, sparse);
    }

    [Fact]
    public void A_wrong_password_opens_nothing()
    {
        using VaultStore? store = VaultStore.Open(
            Path.Combine(FixtureDirectory, "store.bin"), RandomNumberGenerator.GetBytes(64));

        Assert.Null(store);
    }

    [Fact]
    public void The_header_declares_the_cost_that_was_used()
    {
        VaultHeader? header = VaultStore.ReadHeader(Path.Combine(FixtureDirectory, "store.bin"));

        Assert.NotNull(header);
        Assert.Equal(Parameters, header.Kdf);
        Assert.Equal(Convert.FromBase64String(Expected.Salt), header.Salt);
    }

    [Fact]
    public void A_container_we_create_reopens_with_what_we_put_in_it()
    {
        string path = Scratch("nuovo.bin");
        byte[] note = "Una nota nuova, con àccenti"u8.ToArray();

        using (VaultStore created = VaultStore.Create(path, Kek(), Parameters))
        {
            created.SetSection(VaultSectionId.Note, note);
            created.Save();
        }

        using VaultStore? reopened = VaultStore.Open(path, Kek());

        Assert.NotNull(reopened);
        Assert.Equal(note, reopened.GetSection(VaultSectionId.Note));
    }

    [Fact]
    public void Creating_a_container_writes_it_before_anything_is_saved()
    {
        string path = Scratch("subito.bin");

        using VaultStore created = VaultStore.Create(path, Kek(), Parameters);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Changing_the_password_leaves_the_content_untouched()
    {
        string path = Scratch("cambio.bin");
        byte[] note = "Non deve muoversi"u8.ToArray();
        byte[] second = RandomNumberGenerator.GetBytes(64);
        byte[] newSalt = RandomNumberGenerator.GetBytes(16);

        using (VaultStore created = VaultStore.Create(path, Kek(), Parameters))
        {
            created.SetSection(VaultSectionId.Note, note);
            created.Save();
            created.ChangeKeyEncryptionKey(second, Parameters, newSalt);
        }

        using VaultStore? withNew = VaultStore.Open(path, second);
        Assert.NotNull(withNew);
        Assert.Equal(note, withNew.GetSection(VaultSectionId.Note));

        using VaultStore? withOld = VaultStore.Open(path, Kek());
        Assert.Null(withOld);
    }

    [Fact]
    public void A_damaged_container_is_never_overwritten()
    {
        string path = Scratch("rotto.bin");
        using (VaultStore created = VaultStore.Create(path, Kek(), Parameters))
        {
            created.SetSection(VaultSectionId.Note, "qualcosa"u8.ToArray());
            created.Save();
        }

        byte[] raw = File.ReadAllBytes(path);
        raw[^20] ^= 0x01;
        File.WriteAllBytes(path, raw);

        using VaultStore? damaged = VaultStore.Open(path, Kek());

        Assert.NotNull(damaged);
        Assert.True(damaged.IsDamaged);
        Assert.Throws<InvalidOperationException>(() => damaged.Save());
    }

    [Fact]
    public void The_previous_note_is_kept_but_not_at_every_keystroke()
    {
        string path = Scratch("rotazione.bin");
        using VaultStore store = VaultStore.Create(path, Kek(), Parameters);

        store.SetSection(VaultSectionId.Note, "prima"u8.ToArray());
        store.RotatePreviousNote();
        Assert.Equal("prima"u8.ToArray(), store.GetSection(VaultSectionId.PreviousNote));

        store.SetSection(VaultSectionId.Note, "seconda"u8.ToArray());
        store.RotatePreviousNote();

        Assert.Equal("prima"u8.ToArray(), store.GetSection(VaultSectionId.PreviousNote));
    }

    [Fact]
    public void An_emptied_section_disappears_instead_of_being_stored_empty()
    {
        string path = Scratch("svuotata.bin");

        using (VaultStore created = VaultStore.Create(path, Kek(), Parameters))
        {
            created.SetSection(VaultSectionId.Note, "c'era"u8.ToArray());
            created.SetSection(VaultSectionId.Note, []);
            created.Save();
        }

        using VaultStore? reopened = VaultStore.Open(path, Kek());

        Assert.Null(reopened!.GetSection(VaultSectionId.Note));
    }

    [Fact]
    public void Closing_the_container_does_not_wipe_the_caller_arrays()
    {
        string path = Scratch("proprieta.bin");
        byte[] mine = "Questa resta mia"u8.ToArray();
        byte[] untouched = [.. mine];

        using (VaultStore created = VaultStore.Create(path, Kek(), Parameters))
        {
            created.SetSection(VaultSectionId.Note, mine);
            created.Save();
        }

        Assert.Equal(untouched, mine);
    }

    [Fact]
    public void Reading_a_section_twice_gives_independent_copies()
    {
        using VaultStore? store = VaultStore.Open(Path.Combine(FixtureDirectory, "store.bin"), Kek());

        byte[] first = store!.GetSection(VaultSectionId.Note)!;
        first[0] = 0xFF;

        Assert.Equal(Content(Expected.Nota), store.GetSection(VaultSectionId.Note));
    }

    [Fact]
    public void File_timestamps_do_not_say_when_it_was_written()
    {
        string path = Scratch("orologio.bin");

        using VaultStore created = VaultStore.Create(path, Kek(), Parameters);

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData(9, 1024 * 1024 * 1024)]
    [InlineData(9, 0)]
    [InlineData(9, -1)]
    [InlineData(13, 0)]
    [InlineData(13, -5)]
    [InlineData(13, 1000)]
    [InlineData(18, 0)]
    [InlineData(18, -1)]
    public void An_impossible_cost_in_the_header_is_refused(int offset, int value)
    {
        string path = Scratch("costi.bin");
        using (VaultStore created = VaultStore.Create(path, Kek(), Parameters))
        {
            created.SetSection(VaultSectionId.Note, "segreto"u8.ToArray());
            created.Save();
        }

        byte[] tampered = File.ReadAllBytes(path);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(tampered.AsSpan(offset), value);
        File.WriteAllBytes(path, tampered);

        Assert.Null(VaultStore.ReadHeader(path));
    }

    [Fact]
    public void The_default_cost_stays_plausible() => Assert.True(KdfParameters.Default.IsPlausible);
}
