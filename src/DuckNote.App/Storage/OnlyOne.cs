using System.IO;
using DuckNote.Notes.Settings;

namespace DuckNote.App.Storage;

/// <summary>
/// Una copia sola per volta: due copie scriverebbero la stessa cassaforte, e chiudere a chiave
/// l'una lascerebbe aperta l'altra. Il file resta aperto in esclusiva finche' l'app vive e
/// sparisce da se' quando si chiude, anche se si chiude male.
/// </summary>
public sealed class OnlyOne : IDisposable
{
    private readonly FileStream _held;

    private OnlyOne(FileStream held) => _held = held;

    public static OnlyOne? Claim()
    {
        try
        {
            AppPaths.Ensure();

            return new OnlyOne(new FileStream(
                AppPaths.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
        }
        catch (Exception busy) when (busy is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _held.Dispose();
}
