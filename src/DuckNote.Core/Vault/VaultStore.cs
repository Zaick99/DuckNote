using System.Security.Cryptography;
using DuckNote.Core.Crypto;

namespace DuckNote.Core.Vault;

public sealed class VaultStore : IDisposable
{
    private const string EncryptionLabel = "DuckNote/dati/cifratura/1";
    private const string AuthenticationLabel = "DuckNote/dati/integrita/1";

    private const int DataKeyLength = 32;
    private const int SaltLength = 16;

    private static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(15);

    private readonly string _path;
    private readonly VaultSections _sections = new();

    private SecretKey _key;
    private KdfParameters _kdf;
    private byte[] _salt;
    private byte[] _wrapped;

    private DateTime _lastBackup = DateTime.MinValue;
    private DateTime _lastRotation = DateTime.MinValue;
    private bool _disposed;

    private VaultStore(string path, SecretKey key, KdfParameters kdf, byte[] salt, byte[] wrapped, bool damaged)
    {
        _path = path;
        _key = key;
        _kdf = kdf;
        _salt = salt;
        _wrapped = wrapped;
        IsDamaged = damaged;
    }

    public string Path => _path;

    public string BackupPath => _path + ".bak";

    public bool IsDamaged { get; private set; }

    public static bool Exists(string path) => File.Exists(path);

    public static VaultStore Create(string path, byte[] keyEncryptionKey, KdfParameters kdf, byte[]? salt = null)
    {
        ArgumentNullException.ThrowIfNull(keyEncryptionKey);
        ArgumentNullException.ThrowIfNull(kdf);

        byte[] actualSalt = salt ?? RandomNumberGenerator.GetBytes(SaltLength);
        byte[] dataKey = RandomNumberGenerator.GetBytes(DataKeyLength);
        byte[] wrapped = Wrap(dataKey, keyEncryptionKey);

        VaultStore store = new(path, SecretKey.Adopt(dataKey), kdf, actualSalt, wrapped, damaged: false);
        store.Save();
        return store;
    }

    public static VaultStore? Open(string path, byte[] keyEncryptionKey)
    {
        ArgumentNullException.ThrowIfNull(keyEncryptionKey);

        VaultHeader? header = ReadHeader(path);
        if (header is null)
        {
            return null;
        }

        return OpenWith(path, header, keyEncryptionKey);
    }

    public static VaultHeader? ReadHeader(string path)
    {
        try
        {
            return File.Exists(path) ? VaultHeader.Parse(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static VaultStore? OpenWith(string path, VaultHeader header, byte[] keyEncryptionKey)
    {
        (byte[] encryption, byte[] authentication) = KeyDerivation.SplitKeyEncryptionKey(keyEncryptionKey);
        byte[]? dataKey = SealedBlob.Open(header.Wrapped, encryption, authentication);

        if (dataKey is null || dataKey.Length != DataKeyLength)
        {
            return null;
        }

        VaultStore store = new(
            path, SecretKey.Adopt(dataKey), header.Kdf, header.Salt, header.Wrapped, damaged: false);

        store.LoadPayload(header.Payload);
        return store;
    }

    private void LoadPayload(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        byte[]? plain = Unseal(payload);
        if (plain is null)
        {
            IsDamaged = true;
            return;
        }

        try
        {
            VaultSections? parsed = VaultSections.Parse(plain);
            if (parsed is null)
            {
                IsDamaged = true;
                return;
            }

            foreach (byte id in parsed.Ids)
            {
                byte[]? content = parsed.Get((VaultSectionId)id);
                _sections.Set((VaultSectionId)id, content);
                CryptographicOperations.ZeroMemory(content);
            }
            parsed.Clear();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public byte[]? GetSection(VaultSectionId id) => _sections.Get(id);

    public void SetSection(VaultSectionId id, byte[]? content) => _sections.Set(id, content);

    public void RotatePreviousNote(TimeProvider? clock = null)
    {
        DateTime now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (now - _lastRotation < BackupInterval)
        {
            return;
        }

        byte[]? current = _sections.Get(VaultSectionId.Note);
        if (current is null)
        {
            return;
        }

        _sections.Set(VaultSectionId.PreviousNote, current);
        CryptographicOperations.ZeroMemory(current);
        _lastRotation = now;
    }

    public void Save(TimeProvider? clock = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsDamaged)
        {
            throw new InvalidOperationException(
                "Il contenitore e' danneggiato: recupera da store.bin.bak prima di salvare.");
        }

        byte[] serialised = _sections.Serialise();
        byte[] payload;
        try
        {
            payload = Seal(serialised);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serialised);
        }

        Backup(clock);
        SecureFile.WriteAtomically(_path, new VaultHeader(_kdf, _salt, _wrapped, payload).Serialise());
    }

    public void ChangeKeyEncryptionKey(byte[] keyEncryptionKey, KdfParameters kdf, byte[] salt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(keyEncryptionKey);

        byte[] dataKey = _key.Reveal();
        try
        {
            _wrapped = Wrap(dataKey, keyEncryptionKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }

        _kdf = kdf;
        _salt = salt;
        Save();
    }

    private void Backup(TimeProvider? clock)
    {
        DateTime now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (now - _lastBackup < BackupInterval || !File.Exists(_path))
        {
            return;
        }

        try
        {
            File.Copy(_path, BackupPath, overwrite: true);
            SecureFile.PinTimestamps(BackupPath);
            _lastBackup = now;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static byte[] Wrap(byte[] dataKey, byte[] keyEncryptionKey)
    {
        (byte[] encryption, byte[] authentication) = KeyDerivation.SplitKeyEncryptionKey(keyEncryptionKey);
        try
        {
            return SealedBlob.Seal(dataKey, encryption, authentication, padded: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryption);
            CryptographicOperations.ZeroMemory(authentication);
        }
    }

    private byte[] Seal(byte[] plain)
    {
        byte[] encryption = _key.Derive(EncryptionLabel);
        byte[] authentication = _key.Derive(AuthenticationLabel);
        try
        {
            return SealedBlob.Seal(plain, encryption, authentication, padded: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryption);
            CryptographicOperations.ZeroMemory(authentication);
        }
    }

    private byte[]? Unseal(byte[] blob)
    {
        byte[] encryption = _key.Derive(EncryptionLabel);
        byte[] authentication = _key.Derive(AuthenticationLabel);
        try
        {
            return SealedBlob.Open(blob, encryption, authentication);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryption);
            CryptographicOperations.ZeroMemory(authentication);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _sections.Clear();
        _key.Dispose();
        CryptographicOperations.ZeroMemory(_wrapped);
    }
}
