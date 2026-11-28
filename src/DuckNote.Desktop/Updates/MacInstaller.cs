using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using DuckNote.Notes.Updates;

namespace DuckNote.Desktop.Updates;

/// <summary>
/// L'aggiornamento sul Mac. Dal .dmg verificato si estrae il nuovo DuckNote.app (hdiutil in sola
/// lettura, ditto), se ne controlla la firma, e quando DuckNote si e' chiuso uno script scambia i
/// due pacchetti e, se chiesto, riapre l'app. Il vecchio pacchetto si toglie solo quando il nuovo
/// e' al suo posto. Ogni passo ha una seconda strada (l'immagine montata dove decide il sistema,
/// la copia nella cartella temporanea invece che accanto all'app) e se non riesce dice perche':
/// il motivo va nel messaggio, i dettagli in DuckNote-update/aggiornamento.log. Se alla fine lo
/// scambio non si puo' fare (macOS che protegge le app), si apre l'immagine disco: basta
/// trascinare DuckNote in Applicazioni.
/// </summary>
internal sealed class MacInstaller : IUpdateInstaller
{
    private const string Script = """
        #!/bin/bash
        # DuckNote: al posto della versione in uso, quella nuova, appena la vecchia si e' chiusa
        pid="$1"; new="$2"; app="$3"; relaunch="$4"; dmg="$5"; log="$6"
        say() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*" >> "$log"; }
        for i in $(seq 1 600); do kill -0 "$pid" 2>/dev/null || break; sleep 0.1; done
        old="$app.old-$$"
        if mv "$app" "$old" 2>>"$log"; then
          if mv "$new" "$app" 2>>"$log"; then
            rm -rf "$old"
            say "installata: $app"
            [ "$relaunch" = 1 ] && open "$app"
            exit 0
          fi
          mv "$old" "$app" 2>>"$log"
        fi
        # lo scambio non e' riuscito (di solito macOS non lascia toccare le app in Applicazioni
        # senza il permesso "Gestione app"): la nuova versione resta nell'immagine disco, aperta
        say "scambio non riuscito: si apre l'immagine disco"
        rm -rf "$new"
        open "$dmg"
        [ "$relaunch" = 1 ] && open "$app"
        exit 0
        """;

    private readonly string? _bundle;
    private string? _staged;
    private string? _image;

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

    public string? Failure { get; private set; }

    /// <summary>Se l'app non si sostituisce da sola, l'immagine disco scaricata (e verificata) si apre: un trascinamento e basta.</summary>
    public string? OpenHint => _bundle is null ? null : "apri l'aggiornamento e trascina DuckNote in Applicazioni (DuckNote si chiude da solo, salvando)";

    public bool OpenDownloaded(string downloaded)
    {
        try
        {
            ProcessStartInfo open = new("/usr/bin/open") { UseShellExecute = false };
            open.ArgumentList.Add(downloaded);
            using Process? process = Process.Start(open);
            Log($"aperta l'immagine disco per l'installazione a mano: {downloaded}");
            return process is not null;
        }
        catch (Exception problem) when (problem is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string Folder => Path.Combine(Path.GetTempPath(), "DuckNote-update");

    private static string LogFile => Path.Combine(Folder, "aggiornamento.log");

    public async Task<bool> PrepareAsync(string downloaded, ReleaseInfo release, CancellationToken cancel)
    {
        Failure = null;

        if (_bundle is null)
        {
            return false;
        }

        string version = UpdateFeed.Describe(release.Version);
        Log($"--- DuckNote {version}: preparazione da {downloaded}");
        string? mount = null;
        bool ours = false;

        try
        {
            (mount, ours) = await Attach(downloaded, cancel);

            if (mount is null)
            {
                return false;
            }

            string app = Path.Combine(mount, "DuckNote.app");

            if (!Directory.Exists(app))
            {
                return Fail("l'immagine disco non contiene DuckNote.app");
            }

            // accanto all'app in uso, cosi' lo scambio e' un semplice cambio di nome; se li' non
            // si puo' (macOS che protegge la cartella), nella cartella temporanea
            string? staged = await Copy(app, Path.Combine(Path.GetDirectoryName(_bundle)!, $".DuckNote-{version}.app"), cancel)
                ?? await Copy(app, Path.Combine(Folder, $"DuckNote-{version}.app"), cancel);

            if (staged is null)
            {
                return false;
            }

            // la firma (ad hoc) del pacchetto deve reggere: un'app rovinata non prende il posto
            // di quella buona (e su Apple Silicon non partirebbe nemmeno)
            Outcome verify = await Run("/usr/bin/codesign", ["--verify", "--deep", "--strict", staged], cancel);

            if (verify.Code != 0)
            {
                TryDelete(staged);
                return Fail($"la firma della nuova versione non torna ({Short(verify)})");
            }

            _staged = staged;
            _image = downloaded;
            Log($"pronta: {staged}");
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Fail(problem.Message);
        }
        finally
        {
            if (mount is not null)
            {
                await Run("/usr/bin/hdiutil", ["detach", mount, "-force"], CancellationToken.None);

                if (ours)
                {
                    TryDelete(mount);
                }
            }
        }
    }

    /// <summary>
    /// L'immagine montata in una cartella nostra; se hdiutil non vuole, dove decide il sistema
    /// (/Volumes), leggendo il punto di montaggio dalla sua risposta. Null se non si monta.
    /// </summary>
    private async Task<(string? Mount, bool Ours)> Attach(string image, CancellationToken cancel)
    {
        string mount = Path.Combine(Folder, "mnt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(mount);

        // l'impronta del .dmg l'ha gia' controllata il download: hdiutil non la rifa'
        Outcome first = await Run("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-noverify", "-mountpoint", mount, image], cancel);

        if (first.Code == 0)
        {
            return (mount, true);
        }

        TryDelete(mount);
        Outcome second = await Run("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-noverify", "-plist", image], cancel);

        if (second.Code == 0 && MountPoint(second.Output) is { } where)
        {
            return (where, false);
        }

        Fail($"l'immagine disco non si monta ({Short(second.Code != 0 ? second : first)})");
        return (null, false);
    }

    /// <summary>Il punto di montaggio nella risposta di "hdiutil attach -plist".</summary>
    internal static string? MountPoint(string plist)
    {
        try
        {
            XElement[] nodes = [.. XDocument.Parse(plist).Descendants()];

            for (int i = 0; i < nodes.Length - 1; i++)
            {
                if (nodes[i].Name.LocalName == "key" && nodes[i].Value == "mount-point" && nodes[i + 1].Name.LocalName == "string")
                {
                    return nodes[i + 1].Value;
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            // una risposta che non e' un plist: nessun punto di montaggio
        }

        return null;
    }

    /// <summary>Il pacchetto copiato (con ditto, che tiene firma e attributi) in <paramref name="target"/>; null se non riesce.</summary>
    private async Task<string?> Copy(string app, string target, CancellationToken cancel)
    {
        TryDelete(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Outcome copy = await Run("/usr/bin/ditto", [app, target], cancel);

        if (copy.Code == 0 && Directory.Exists(target))
        {
            return target;
        }

        TryDelete(target);
        Fail($"la nuova versione non si copia in {Path.GetDirectoryName(target)} ({Short(copy)})");
        return null;
    }

    public bool Install(bool relaunch)
    {
        if (_bundle is null || _staged is null || !Directory.Exists(_staged))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            string script = Path.Combine(Folder, "install.sh");
            File.WriteAllText(script, Script.Replace("\r\n", "\n", StringComparison.Ordinal));

            ProcessStartInfo start = new("/bin/bash") { UseShellExecute = false };
            start.ArgumentList.Add(script);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(_staged);
            start.ArgumentList.Add(_bundle);
            start.ArgumentList.Add(relaunch ? "1" : "0");
            start.ArgumentList.Add(_image ?? string.Empty);
            start.ArgumentList.Add(LogFile);

            // lo script sopravvive a DuckNote: aspetta che si chiuda, poi scambia i pacchetti
            using Process? process = Process.Start(start);
            Log("installazione affidata allo script, alla chiusura");
            return process is not null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Fail(problem.Message);
            return false;
        }
    }

    private bool Fail(string why)
    {
        Failure = why;
        Log($"non riuscito: {why}");
        return false;
    }

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n");
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // senza registro l'aggiornamento va avanti lo stesso
        }
    }

    /// <summary>La prima riga utile di quello che ha detto il programma, per il messaggio.</summary>
    private static string Short(Outcome outcome)
    {
        string text = (outcome.Errors.Trim().Length > 0 ? outcome.Errors : outcome.Output).Trim();
        string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        string code = outcome.Code < 0 ? "tempo scaduto" : $"codice {outcome.Code}";
        return first.Length == 0 ? code : $"{code}: {(first.Length > 140 ? first[..140] + "..." : first)}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // un punto di montaggio rimasto occupato se ne va al riavvio del Mac
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

    private readonly record struct Outcome(int Code, string Output, string Errors);

    private static async Task<Outcome> Run(string program, IReadOnlyList<string> arguments, CancellationToken cancel)
    {
        ProcessStartInfo start = new(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} non parte");
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Log($"{Path.GetFileName(program)} {string.Join(' ', arguments)}: fermato dopo 3 minuti");
            return new Outcome(-1, string.Empty, string.Empty);
        }

        Outcome outcome = new(process.ExitCode, await output, await errors);
        Log($"{Path.GetFileName(program)} {string.Join(' ', arguments)} -> {outcome.Code}{(outcome.Errors.Trim().Length > 0 ? $" | {outcome.Errors.Trim()}" : string.Empty)}");
        return outcome;
    }
}
