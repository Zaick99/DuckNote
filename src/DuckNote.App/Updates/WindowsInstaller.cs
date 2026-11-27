using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DuckNote.Notes.Updates;

namespace DuckNote.App.Updates;

/// <summary>
/// L'aggiornamento su Windows. La copia portabile si sostituisce da se': l'eseguibile in uso non
/// si puo' sovrascrivere ma si puo' rinominare, quindi va da parte come DuckNote.exe.old, il
/// nuovo (verificato) prende il suo posto, e la copia vecchia si cancella all'avvio dopo. La
/// copia installata (accanto c'e' unins000.exe di Inno Setup) passa dall'installer nuovo,
/// silenzioso, che rifa' l'installazione com'era: per l'utente o per tutti.
/// </summary>
internal sealed class WindowsInstaller : IUpdateInstaller
{
    private readonly string? _exe;
    private string? _downloaded;

    public WindowsInstaller()
    {
        _exe = Environment.ProcessPath;

        if (_exe is null || !_exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Blocker = "DuckNote non gira dal suo eseguibile: la nuova versione si scarica dalla pagina della release.";
            return;
        }

        string folder = Path.GetDirectoryName(_exe)!;
        bool installed = File.Exists(Path.Combine(folder, "unins000.exe"));

        if (!installed && !Writable(folder))
        {
            Blocker = "La cartella di DuckNote non si puo' scrivere: la nuova versione si scarica dalla pagina della release.";
            return;
        }

        Target = installed ? UpdateTarget.WindowsInstaller : UpdateTarget.WindowsPortable;
    }

    public UpdateTarget? Target { get; }

    public string? Blocker { get; }

    public string? Failure { get; private set; }

    /// <summary>Se non si installa da sola: l'installer si apre (guidato), il nuovo eseguibile si mostra in Esplora file.</summary>
    public string? OpenHint => Target switch
    {
        UpdateTarget.WindowsInstaller => "apri l'aggiornamento: l'installazione e' guidata",
        UpdateTarget.WindowsPortable => "apri l'aggiornamento: e' il nuovo DuckNote.exe, da mettere al posto di quello in uso",
        _ => null
    };

    public bool OpenDownloaded(string downloaded)
    {
        try
        {
            ProcessStartInfo open = Target == UpdateTarget.WindowsInstaller
                ? new ProcessStartInfo(downloaded) { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe") { UseShellExecute = false, Arguments = $"/select,\"{downloaded}\"" };
            using Process? started = Process.Start(open);
            return started is not null;
        }
        catch (Exception problem) when (problem is IOException or Win32Exception)
        {
            return false;
        }
    }

    public Task<bool> PrepareAsync(string downloaded, ReleaseInfo release, CancellationToken cancel)
    {
        _downloaded = downloaded;
        Failure = File.Exists(downloaded) ? null : "il file scaricato non c'e' piu'";
        return Task.FromResult(Failure is null);
    }

    public bool Install(bool relaunch)
    {
        if (_exe is null || _downloaded is null || !File.Exists(_downloaded))
        {
            return false;
        }

        try
        {
            if (Target == UpdateTarget.WindowsInstaller)
            {
                // nessuna finestra; /RELAUNCH=1 la riapre a installazione finita (vedi DuckNote.iss)
                ProcessStartInfo setup = new(_downloaded)
                {
                    UseShellExecute = true,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" + (relaunch ? " /RELAUNCH=1" : string.Empty)
                };
                using Process? started = Process.Start(setup);
                return started is not null;
            }

            string old = _exe + ".old";

            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(_exe, old);

            try
            {
                File.Copy(_downloaded, _exe, overwrite: false);
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
            {
                // il nuovo non si e' potuto mettere: torna quello di prima, intatto
                File.Move(old, _exe);
                return false;
            }

            if (relaunch)
            {
                // la copia nuova aspetta che questa sia uscita prima di aprire la nota
                ProcessStartInfo again = new(_exe) { UseShellExecute = false };
                again.ArgumentList.Add("--after");
                again.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                using Process? started = Process.Start(again);
            }

            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>All'avvio: la copia vecchia lasciata dall'ultimo aggiornamento se ne va.</summary>
    public static void CleanUp()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            File.Delete(exe + ".old");
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // ancora aperta (la copia di prima non e' uscita del tutto): al prossimo avvio
        }
    }

    /// <summary>"--after 1234": si aspetta che la copia che si e' appena aggiornata sia uscita.</summary>
    public static void WaitForPrevious(string[] args)
    {
        int at = Array.IndexOf(args, "--after");

        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
        {
            return;
        }

        try
        {
            using Process previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (Exception problem) when (problem is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // gia' uscita
        }
    }

    private static bool Writable(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, ".ducknote-write-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
