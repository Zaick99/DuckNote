using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DuckNote.Notes.Updates;

namespace DuckNote.Desktop.Updates;

/// <summary>
/// L'aggiornamento sul Mac. Dal .dmg verificato si estrae il nuovo DuckNote.app accanto a quello
/// in uso (hdiutil in sola lettura, ditto), se ne controlla la firma, e quando DuckNote si e'
/// chiuso uno script scambia i due pacchetti e, se chiesto, riapre l'app. Il vecchio pacchetto
/// si toglie solo quando il nuovo e' al suo posto. Un'app aperta dall'immagine disco o da una
/// cartella che non si puo' scrivere non si aggiorna da sola: resta la pagina della release.
/// </summary>
internal sealed class MacInstaller : IUpdateInstaller
{
    private const string Script = """
        #!/bin/bash
        # DuckNote: al posto della versione in uso, quella nuova, appena la vecchia si e' chiusa
        pid="$1"; new="$2"; app="$3"; relaunch="$4"
        for i in $(seq 1 600); do kill -0 "$pid" 2>/dev/null || break; sleep 0.1; done
        old="$app.old-$$"
        if mv "$app" "$old"; then
          if mv "$new" "$app"; then
            rm -rf "$old"
          else
            mv "$old" "$app"
          fi
        fi
        [ "$relaunch" = 1 ] && open "$app"
        exit 0
        """;

    private readonly string? _bundle;
    private string? _staged;

    public MacInstaller()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Blocker = "Su questo sistema la nuova versione si scarica dalla pagina della release.";
            return;
        }

        string bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..")).TrimEnd('/');

        if (!bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            Blocker = "DuckNote non gira da DuckNote.app: la nuova versione si scarica dalla pagina della release.";
            return;
        }

        // aperta senza spostarla (macOS la "trasloca" in una copia in sola lettura); l'immagine
        // disco stessa e' in sola lettura, e la prova di scrittura qui sotto la esclude
        if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal))
        {
            Blocker = "DuckNote e' aperta dall'immagine disco o da Download: spostala in Applicazioni e si aggiornera' da sola.";
            return;
        }

        if (!Writable(Path.GetDirectoryName(bundle)!))
        {
            Blocker = "La cartella di DuckNote non si puo' scrivere: la nuova versione si scarica dalla pagina della release.";
            return;
        }

        _bundle = bundle;
        Target = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? UpdateTarget.MacArm64 : UpdateTarget.MacX64;
    }

    public UpdateTarget? Target { get; }

    public string? Blocker { get; }

    public async Task<bool> PrepareAsync(string downloaded, ReleaseInfo release, CancellationToken cancel)
    {
        if (_bundle is null)
        {
            return false;
        }

        string staged = Path.Combine(Path.GetDirectoryName(_bundle)!, $".DuckNote-{UpdateFeed.Describe(release.Version)}.app");
        string mount = Path.Combine(Path.GetTempPath(), "DuckNote-update", "mnt-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            if (Directory.Exists(staged))
            {
                Directory.Delete(staged, recursive: true);
            }

            Directory.CreateDirectory(mount);

            // l'impronta del .dmg l'ha gia' controllata il download: hdiutil non la rifa'
            if (await Run("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-noverify", "-mountpoint", mount, downloaded], cancel) != 0)
            {
                return false;
            }

            try
            {
                string app = Path.Combine(mount, "DuckNote.app");

                if (!Directory.Exists(app) || await Run("/usr/bin/ditto", [app, staged], cancel) != 0)
                {
                    return false;
                }
            }
            finally
            {
                await Run("/usr/bin/hdiutil", ["detach", mount, "-force"], CancellationToken.None);
            }

            // la firma (ad hoc) del pacchetto deve reggere: un'app rovinata non prende il posto di quella buona
            if (await Run("/usr/bin/codesign", ["--verify", "--deep", "--strict", staged], cancel) != 0)
            {
                Directory.Delete(staged, recursive: true);
                return false;
            }

            _staged = staged;
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(mount))
                {
                    Directory.Delete(mount);
                }
            }
            catch (IOException)
            {
                // un punto di montaggio rimasto occupato se ne va al riavvio del Mac
            }
        }
    }

    public bool Install(bool relaunch)
    {
        if (_bundle is null || _staged is null || !Directory.Exists(_staged))
        {
            return false;
        }

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "DuckNote-update");
            Directory.CreateDirectory(folder);
            string script = Path.Combine(folder, "install.sh");
            File.WriteAllText(script, Script.Replace("\r\n", "\n", StringComparison.Ordinal));

            ProcessStartInfo start = new("/bin/bash") { UseShellExecute = false };
            start.ArgumentList.Add(script);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(_staged);
            start.ArgumentList.Add(_bundle);
            start.ArgumentList.Add(relaunch ? "1" : "0");

            // lo script sopravvive a DuckNote: aspetta che si chiuda, poi scambia i pacchetti
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool Writable(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, ".DuckNote-write-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<int> Run(string program, IReadOnlyList<string> arguments, CancellationToken cancel)
    {
        ProcessStartInfo start = new(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} non parte");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancel);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancel);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }

        await Task.WhenAll(output, errors);
        return process.ExitCode;
    }
}
