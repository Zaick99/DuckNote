using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;
using DuckNote.Notes.Settings;

namespace DuckNote.App.Vault;

/// <summary>
/// La cassaforte della nota, se c'e'. Mentre esiste un contenitore nulla si scrive in chiaro:
/// nota, nota precedente, ultima scansione e host messi da parte stanno tutti in store.bin.
/// </summary>
public sealed class VaultSession : IDisposable
{
    private VaultStore? _store;

    public static bool Exists => File.Exists(AppPaths.Store);

    public bool IsOpen => _store is not null;

    public bool IsDamaged => _store?.IsDamaged ?? false;

    public static VaultHeader? Header() => VaultStore.ReadHeader(AppPaths.Store);

    /// <summary>Da password a chiave che apre la chiave: Argon2id e poi PBKDF2, in un thread a parte.</summary>
    public static Task<byte[]> DeriveAsync(SecureString password, byte[] salt, KdfParameters kdf) =>
        KeyDerivation.DeriveKeyEncryptionKeyAsync(PasswordBytes.FromSecureString(password), salt, kdf);

    public static byte[] FreshSalt() => RandomNumberGenerator.GetBytes(32);

    public bool Unlock(byte[] keyEncryptionKey)
    {
        if (VaultStore.Open(AppPaths.Store, keyEncryptionKey) is not { } opened)
        {
            return false;
        }

        _store?.Dispose();
        _store = opened;
        RemoveLeftovers();

        return true;
    }

    public void Create(byte[] keyEncryptionKey, KdfParameters kdf, byte[] salt, byte[] note, string lastScan, string hidden)
    {
        AppPaths.Ensure();
        _store?.Dispose();
        _store = VaultStore.Create(AppPaths.Store, keyEncryptionKey, kdf, salt);
        _store.SetSection(VaultSectionId.Note, note);
        LastScan = lastScan;
        Hidden = hidden;
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

    public byte[]? Note => _store?.GetSection(VaultSectionId.Note);

    public void WriteNote(byte[] note)
    {
        if (_store is null || _store.IsDamaged)
        {
            return;
        }

        _store.RotatePreviousNote();
        _store.SetSection(VaultSectionId.Note, note);
        _store.Save();
    }

    public string LastScan
    {
        get => Text(VaultSectionId.LastScan);
        set => SetText(VaultSectionId.LastScan, value);
    }

    /// <summary>Gli host messi da parte, separati da virgole (la sezione "privata" del contenitore).</summary>
    public string Hidden
    {
        get => Text(VaultSectionId.Private);
        set => SetText(VaultSectionId.Private, value);
    }

    public void Save()
    {
        if (_store is { IsDamaged: false } store)
        {
            store.Save();
        }
    }

    /// <summary>
    /// La nota torna in chiaro e il contenitore viene distrutto. Prima si scrive la nota e si
    /// controlla che ci sia: se non riesce, il contenitore resta dov'e'.
    /// </summary>
    public bool Disable(byte[] note)
    {
        if (_store is null)
        {
            return false;
        }

        try
        {
            AppPaths.Ensure();
            File.WriteAllBytes(AppPaths.Note, note);

            if (new FileInfo(AppPaths.Note).Length == 0)
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

    /// <summary>Le copie in chiaro rimaste accanto al contenitore vengono coperte e tolte.</summary>
    public static void RemoveLeftovers()
    {
        if (!Exists)
        {
            return;
        }

        foreach (string path in (string[])[AppPaths.Note, AppPaths.NoteBackup, AppPaths.LastScan])
        {
            if (File.Exists(path))
            {
                SecureFile.Shred(path);
            }
        }
    }

    public void Dispose() => Lock();

    private string Text(VaultSectionId id)
    {
        if (_store?.GetSection(id) is not { } bytes)
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
}
