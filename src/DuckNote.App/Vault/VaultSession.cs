using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using DuckNote.App.Storage;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;

namespace DuckNote.App.Vault;

public sealed record PendingVault(byte[] Key, KdfParameters Kdf, byte[] Salt);

public sealed class VaultSession : IDisposable
{
    private VaultStore? _store;

    public PendingVault? Pending { get; set; }

    public static bool Exists => File.Exists(AppPaths.Store);

    public bool IsOpen => _store is not null;

    public bool IsLocked => Exists && _store is null;

    public bool IsDamaged => _store?.IsDamaged ?? false;

    public static VaultHeader? Header() => VaultStore.ReadHeader(AppPaths.Store);

    public static Task<byte[]> DeriveAsync(SecureString password, byte[] salt, KdfParameters kdf,
        CancellationToken cancellationToken = default) =>
        KeyDerivation.DeriveKeyEncryptionKeyAsync(
            PasswordBytes.FromSecureString(password), salt, kdf, cancellationToken);

    public bool Unlock(byte[] keyEncryptionKey)
    {
        VaultStore? opened = VaultStore.Open(AppPaths.Store, keyEncryptionKey);
        if (opened is null)
        {
            return false;
        }

        _store?.Dispose();
        _store = opened;

        RemoveLeftovers();
        return true;
    }

    public void Create(byte[] keyEncryptionKey, KdfParameters kdf, byte[] salt, byte[]? note)
    {
        AppPaths.Ensure();
        _store?.Dispose();
        _store = VaultStore.Create(AppPaths.Store, keyEncryptionKey, kdf, salt);

        if (note is { Length: > 0 })
        {
            _store.SetSection(VaultSectionId.Note, note);
        }
        _store.Save();

        RemoveLeftovers();
    }

    public void ChangePassword(byte[] keyEncryptionKey, KdfParameters kdf, byte[] salt) =>
        _store?.ChangeKeyEncryptionKey(keyEncryptionKey, kdf, salt);

    public void Lock()
    {
        _store?.Dispose();
        _store = null;
    }

    public byte[]? Note
    {
        get => _store?.GetSection(VaultSectionId.Note);
        set => _store?.SetSection(VaultSectionId.Note, value);
    }

    public byte[]? PreviousNote => _store?.GetSection(VaultSectionId.PreviousNote);

    public string LastScan
    {
        get => Text(VaultSectionId.LastScan);
        set => SetText(VaultSectionId.LastScan, value);
    }

    public string PrivateData
    {
        get => Text(VaultSectionId.Private);
        set => SetText(VaultSectionId.Private, value);
    }

    public void RotatePreviousNote() => _store?.RotatePreviousNote();

    public void Save()
    {
        if (_store is null || _store.IsDamaged)
        {
            return;
        }
        _store.Save();
    }

    private string Text(VaultSectionId id)
    {
        byte[]? bytes = _store?.GetSection(id);
        if (bytes is null)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private void SetText(VaultSectionId id, string value) =>
        _store?.SetSection(id, value.Length == 0 ? null : Encoding.UTF8.GetBytes(value));

    public bool Disable(byte[] noteBytes)
    {
        if (_store is null)
        {
            return false;
        }

        try
        {
            AppPaths.Ensure();
            File.WriteAllBytes(AppPaths.Note, noteBytes);

            if (!File.Exists(AppPaths.Note) || new FileInfo(AppPaths.Note).Length == 0)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        _store.Dispose();
        _store = null;

        SecureFile.Shred(AppPaths.Store);
        SecureFile.Shred(AppPaths.Store + ".bak");
        return true;
    }

    public static IReadOnlyList<string> RemoveLeftovers()
    {
        if (!Exists)
        {
            return [];
        }

        List<string> removed = [];
        foreach (string path in (string[])[AppPaths.Note, AppPaths.NoteBackup, AppPaths.LastScan])
        {
            if (!File.Exists(path))
            {
                continue;
            }

            SecureFile.Shred(path);
            removed.Add(Path.GetFileName(path));
        }

        return removed;
    }

    public bool CommitPending(byte[] note)
    {
        if (Pending is not { } waiting)
        {
            return false;
        }

        try
        {
            Create(waiting.Key, waiting.Kdf, waiting.Salt, note);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(waiting.Key);
            Pending = null;
        }
    }

    public void Dispose()
    {
        if (Pending is { } waiting)
        {
            CryptographicOperations.ZeroMemory(waiting.Key);
            Pending = null;
        }

        _store?.Dispose();
        _store = null;
    }
}
