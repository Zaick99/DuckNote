using System.IO;
using DuckNote.Core.Vault;
using DuckNote.Notes.Settings;

namespace DuckNote.App.Storage;

/// <summary>La disinstallazione dei dati: ogni file della cartella sovrascritto, poi la cartella via.</summary>
public static class AppWipe
{
    public static void Remove()
    {
        if (!Directory.Exists(AppPaths.Folder))
        {
            return;
        }

        try
        {
            foreach (string path in Directory.EnumerateFiles(AppPaths.Folder, "*", SearchOption.AllDirectories))
            {
                if (!path.Equals(AppPaths.Lock, StringComparison.OrdinalIgnoreCase))
                {
                    SecureFile.Shred(path);
                }
            }

            Directory.Delete(AppPaths.Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // la cartella col file di blocco aperto resta: sparisce col prossimo avvio vuoto
        }
    }
}
