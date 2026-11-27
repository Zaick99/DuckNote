namespace DuckNote.Notes.Updates;

/// <summary>
/// Come si installa una versione nuova su questo sistema: lo sa l'app (Windows o Mac). Target
/// null vuol dire che qui non si puo' (un'app aperta dall'immagine disco, una cartella in sola
/// lettura): Blocker dice perche', e resta il link alla pagina della release.
/// </summary>
public interface IUpdateInstaller
{
    UpdateTarget? Target { get; }

    string? Blocker { get; }

    /// <summary>Dopo il download verificato: quello che si puo' fare prima di chiudere (sul Mac, estrarre l'app).</summary>
    Task<bool> PrepareAsync(string downloaded, ReleaseInfo release, CancellationToken cancel);

    /// <summary>
    /// Installa, mentre DuckNote si sta chiudendo (la nota e' gia' salvata); con
    /// <paramref name="relaunch"/> la riapre, appena la copia vecchia e' uscita.
    /// </summary>
    bool Install(bool relaunch);

    /// <summary>Perche' l'ultima preparazione non e' riuscita, in poche parole (null se non c'e' niente da dire).</summary>
    string? Failure => null;

    /// <summary>
    /// Cosa fare a mano col file gia' scaricato e verificato, se qui si puo' aprirlo (sul Mac:
    /// l'immagine disco, da cui si trascina DuckNote in Applicazioni); null se non si puo'.
    /// </summary>
    string? OpenHint => null;

    /// <summary>Apre il file scaricato per installarlo a mano (vedi <see cref="OpenHint"/>).</summary>
    bool OpenDownloaded(string downloaded) => false;
}

/// <summary>
/// L'aggiornamento di un avvio: chiede a GitHub l'ultima release, la scarica se e' piu' nuova,
/// la verifica con la sua impronta, la prepara. Pronta, si installa quando l'utente sceglie
/// "Riavvia e aggiorna" oppure alla chiusura. Ogni passo che non riesce lascia tutto com'era e
/// dice dove scaricarla a mano.
/// </summary>
public sealed class UpdateSession(IUpdateInstaller installer, Version current, Func<Version, HttpClient>? client = null)
{
    private bool _installed;
    private bool _relaunch;

    /// <summary>Il file scaricato e verificato dell'ultimo tentativo.</summary>
    private string? _downloaded;

    /// <summary>La versione che non si e' potuta preparare: in questo avvio non si riscarica.</summary>
    private Version? _failed;
    private string _failure = string.Empty;

    public Version Current { get; } = current;

    /// <summary>La release piu' nuova di quella in uso, se c'e'.</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>Scaricata, verificata e preparata: aspetta solo di essere installata.</summary>
    public bool IsReady { get; private set; }

    public bool IsChecking { get; private set; }

    public string Message { get; private set; } = string.Empty;

    /// <summary>Perche' qui non si aggiorna da sola, se e' cosi'.</summary>
    public string? Blocker => installer.Blocker;

    /// <summary>La versione nuova e' scaricata e verificata ma non si e' preparata: si puo' aprire e installare a mano.</summary>
    public bool CanOpenDownloaded => _failed is not null && installer.OpenHint is not null && _downloaded is { } file && File.Exists(file);

    /// <summary>Apre il file scaricato (sul Mac l'immagine disco) per installarlo a mano.</summary>
    public bool OpenDownloaded() => CanOpenDownloaded && installer.OpenDownloaded(_downloaded!);

    /// <summary>Lo stato e' cambiato (sul thread di chi ha chiamato CheckAsync).</summary>
    public event Action? Changed;

    public async Task CheckAsync(CancellationToken cancel = default)
    {
        if (IsChecking || IsReady)
        {
            return;
        }

        IsChecking = true;
        Say("Controllo degli aggiornamenti...");

        try
        {
            using HttpClient http = (client ?? Updater.Client)(Current);
            Updater updater = new(http);
            ReleaseInfo? latest = await updater.LatestAsync(cancel);

            if (latest is null)
            {
                Say("GitHub non ha risposto: si riprova al prossimo avvio.");
                return;
            }

            if (!UpdateFeed.IsNewer(latest, Current))
            {
                Available = null;
                Say($"DuckNote {UpdateFeed.Describe(Current)} e' l'ultima versione.");
                return;
            }

            Available = latest;
            string version = UpdateFeed.Describe(latest.Version);

            if (installer.Target is not { } target)
            {
                Say($"E' uscita DuckNote {version}. {installer.Blocker}");
                return;
            }

            // gia' provata in questo avvio e non preparata: non si riscarica, resta l'installazione a mano
            if (_failed == latest.Version)
            {
                Say(_failure);
                return;
            }

            if (UpdateFeed.AssetFor(latest, target) is not { } asset)
            {
                Say($"E' uscita DuckNote {version}, ma non c'e' il file per questo sistema: si scarica dalla pagina della release.");
                return;
            }

            Say($"Download di DuckNote {version}...");
            string folder = Path.Combine(Path.GetTempPath(), "DuckNote-update", version);
            string file = await updater.DownloadAsync(latest, asset, folder, cancel);
            _downloaded = file;

            if (!await installer.PrepareAsync(file, latest, cancel))
            {
                _failed = latest.Version;
                string why = installer.Failure is { Length: > 0 } reason ? $" ({reason})" : string.Empty;
                _failure = installer.OpenHint is { } hint
                    ? $"DuckNote {version} non si e' installata da sola{why}: {hint}."
                    : $"DuckNote {version} non si e' potuta preparare{why}: si scarica dalla pagina della release.";
                Say(_failure);
                return;
            }

            IsReady = true;
            Say($"DuckNote {version} e' pronta: si installa quando chiudi DuckNote.");
        }
        catch (Exception problem) when (problem is HttpRequestException or TaskCanceledException or IOException
                                            or UnauthorizedAccessException or InvalidDataException)
        {
            Say(problem is InvalidDataException
                ? $"Aggiornamento scartato: {problem.Message}."
                : "Aggiornamento non riuscito: si riprova al prossimo avvio.");
        }
        finally
        {
            IsChecking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// "Riavvia e aggiorna": la versione nuova si installa alla chiusura, che l'app fa subito
    /// dopo, e DuckNote si riapre. Installare mentre la nota si sta ancora salvando no: Windows
    /// chiuderebbe l'app a meta'. Falso se non c'e' niente di pronto.
    /// </summary>
    public bool InstallNow()
    {
        if (!IsReady || _installed)
        {
            return false;
        }

        _relaunch = true;
        return true;
    }

    /// <summary>Alla chiusura, a nota salvata: se una versione e' pronta, adesso (e si riapre se lo si era chiesto).</summary>
    public void InstallOnExit()
    {
        if (!IsReady || _installed)
        {
            return;
        }

        _installed = installer.Install(_relaunch);

        if (!_installed)
        {
            Say("L'installazione non e' partita: si scarica dalla pagina della release.");
        }
    }

    private void Say(string message)
    {
        Message = message;
        Changed?.Invoke();
    }
}
