using System.IO;
using DuckNote.Core.Vault;

namespace DuckNote.App.Storage;

public static class AppWipe
{
    public static IReadOnlyList<(string Name, long Bytes)> Inventory()
    {
        if (!Directory.Exists(AppPaths.Folder))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(AppPaths.Folder, "*", SearchOption.AllDirectories)
                    .Select(path => (Path.GetFileName(path), new FileInfo(path).Length))
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

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
                SecureFile.Shred(path);
            }

            Directory.Delete(AppPaths.Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
