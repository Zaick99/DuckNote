using System.IO;

namespace DuckNote.App.Storage;

public sealed class OnlyOne : IDisposable
{
    private readonly FileStream _held;

    private bool _let;

    private OnlyOne(FileStream held) => _held = held;

    public static string Path => System.IO.Path.Combine(AppPaths.Folder, "aperto.lock");

    public static OnlyOne? Claim()
    {
        try
        {
            AppPaths.Ensure();

            return new OnlyOne(new FileStream(
                Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
        }
        catch (Exception busy) when (busy is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_let)
        {
            return;
        }

        _let = true;
        _held.Dispose();
    }
}
