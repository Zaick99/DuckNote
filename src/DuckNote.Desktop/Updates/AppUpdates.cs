using DuckNote.Notes.Updates;

namespace DuckNote.Desktop.Updates;

/// <summary>L'aggiornamento di questo avvio, uno per tutta l'app (finestra, Impostazioni, chiusura).</summary>
internal static class AppUpdates
{
    public static Version Current { get; } = typeof(AppUpdates).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static UpdateSession Session { get; } = new(new MacInstaller(), Current);
}
