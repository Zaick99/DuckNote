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

            // la cartella duck e' condivisa con le altre app Duck: va via solo se resta vuota
            if (Path.GetDirectoryName(AppPaths.Folder) is { } duck
                && Path.GetFileName(duck).Equals("duck", StringComparison.OrdinalIgnoreCase)
                && !Directory.EnumerateFileSystemEntries(duck).Any())
            {
                Directory.Delete(duck);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // la cartella col file di blocco aperto resta: sparisce col prossimo avvio vuoto
        }
    }
}
