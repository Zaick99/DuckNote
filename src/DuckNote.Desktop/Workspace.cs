using Avalonia.Threading;
using DuckNote.App.Scanning;
using DuckNote.App.Storage;
using DuckNote.App.Vault;
using DuckNote.Desktop.Notes;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Scanning;
using DuckNote.Notes.Settings;
using DuckNote.Notes.Sheets;

namespace DuckNote.Desktop;

/// <summary>
/// Lo stato dell'applicazione, condiviso dalle pagine: impostazioni, nota, cassaforte, tabella
/// degli host. Le pagine mostrano e chiedono; qui si decide cosa si salva, cosa si analizza e
/// quando. E' il Workspace della versione WPF, con la nota fatta di <see cref="SheetDoc"/>
/// invece che di FlowDocument: stesso file, stessa cassaforte, stesse regole.
/// </summary>
public sealed class Workspace
{
    private static readonly TimeSpan BackupEvery = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _fresh = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _monitor = new();
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>Raggruppa gli avvisi "gli host sono cambiati": durante un'analisi ne arrivano a decine al secondo.</summary>
    private readonly DispatcherTimer _hosts = new() { Interval = TimeSpan.FromMilliseconds(150) };

    /// <summary>Le scritture su disco, fuori dal thread dell'interfaccia.</summary>
    private readonly IoQueue _io = new();

    private DateTime _lastActivity = DateTime.UtcNow;
    private DateTime _lastBackup = DateTime.MinValue;
    private bool _dirty;
    private bool _wiped;

    public Workspace(AppSettings settings)
    {
        Settings = settings;

        _save.Tick += (_, _) =>
        {
            _save.Stop();
            SaveNote();
        };

        _fresh.Tick += async (_, _) =>
        {
            _fresh.Stop();

            if (IsLocked)
            {
                return;
            }

            if (Session.IsRunning)
            {
                // c'e' gia' un'analisi (quella dell'indirizzo scritto prima, o il monitoraggio):
                // i nuovi aspettano che finisca, non il giro dopo del monitoraggio
                _fresh.Start();
                return;
            }

            IReadOnlyList<string> waiting = Session.NeverChecked(NoteHosts());

            if (waiting.Count > 0)
            {
                await Scan(string.Join(',', waiting), keep: true, automatic: true);
            }
        };

        _monitor.Tick += async (_, _) =>
        {
            // gli indirizzi gia' in tabella: non la parola che si sta ancora scrivendo
            if (Session.IsRunning || IsLocked || NoteHosts().Where(host => Session.Find(host) is not null).ToList() is not { Count: > 0 } hosts)
            {
                return;
            }

            await Scan(string.Join(',', hosts), keep: true, automatic: true);
        };

        _idle.Tick += (_, _) =>
        {
            if (Vault.IsOpen && Settings.AutoLockMinutes > 0
                && (DateTime.UtcNow - _lastActivity).TotalMinutes >= Settings.AutoLockMinutes)
            {
                Lock($"ferma da {Settings.AutoLockMinutes} minuti");
            }
        };

        Session.Finished += _ =>
        {
            KeepLastScan();
            RaiseHosts();
        };

        Session.Rows.CollectionChanged += (_, _) => RaiseHosts();
        Session.PropertyChanged += (_, _) => RaiseHosts();

        _hosts.Tick += (_, _) =>
        {
            _hosts.Stop();
            HostsChanged?.Invoke();
        };

        _io.Failed += (what, problem) => Dispatcher.UIThread.Post(() =>
        {
            if (what == "nota")
            {
                _dirty = true;
                Unsaved?.Invoke();
            }

            Say(problem is IOException or UnauthorizedAccessException
                ? "Non salvato: la cartella dei dati non e' scrivibile. Riprovo al prossimo salvataggio."
                : $"Non salvato: {problem.Message}");
        });
    }

    private void RaiseHosts()
    {
        if (!_hosts.IsEnabled)
        {
            _hosts.Start();
        }
    }

    public AppSettings Settings { get; }

    public VaultSession Vault { get; } = new();

    public ScanSession Session { get; } = new();

    public Notebook Book { get; private set; } = Notebook.Of(SheetDoc.Blank());

    /// <summary>Il foglio aperto nell'editor.</summary>
    public Sheet? CurrentSheet { get; set; }

    /// <summary>La nota e' chiusa a chiave: la cassaforte esiste ma la chiave non e' in memoria.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Quando la nota e' stata scritta l'ultima volta (null finche' non succede).</summary>
    public DateTime? LastSaved { get; private set; }

    /// <summary>Il contenitore non ha superato la verifica: si mostra un avviso e non si salva nulla.</summary>
    public bool IsDamaged => Vault.IsDamaged;

    /// <summary>Ci sono modifiche che aspettano di essere scritte.</summary>
    public bool HasUnsaved => _dirty;

    public event Action<string>? Status;

    /// <summary>La nota e' appena stata scritta.</summary>
    public event Action? Saved;

    /// <summary>Nella nota c'e' qualcosa che non e' ancora su disco.</summary>
    public event Action? Unsaved;

    /// <summary>Gli host della tabella o il loro stato sono cambiati.</summary>
    public event Action? HostsChanged;

    /// <summary>La nota e' stata sostituita per intero (apertura, sblocco, blocco).</summary>
    public event Action? NoteReplaced;

    /// <summary>La cassaforte e' cambiata (creata, aperta, chiusa, tolta).</summary>
    public event Action? VaultChanged;

    /// <summary>Si e' chiusa a chiave: la finestra deve coprirsi.</summary>
    public event Action? Locked;

    /// <summary>Qualcuno vuole vedere la scheda di un host.</summary>
    public event Action<string>? HostRequested;

    /// <summary>Si chiede di portare una tabella di risultati nella nota.</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>>? TableToNote;

    public void RequestHost(string host) => HostRequested?.Invoke(host);

    public void SendTable(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows) =>
        TableToNote?.Invoke(header, rows);

    public void Say(string message) => Status?.Invoke(message);

    public void Activity() => _lastActivity = DateTime.UtcNow;

    // ------------------------------------------------------------------ nota

    public void Start()
    {
        Session.RestoreHidden(HostList.Parse(Settings.IgnoredHosts));
        IsLocked = VaultSession.Exists;

        if (!IsLocked)
        {
            OpenNote();
        }

        StartTimers();
    }

    /// <summary>Legge la nota (dalla cassaforte se e' aperta) e l'ultima scansione.</summary>
    public void OpenNote()
    {
        _io.Drain();
        byte[]? stored = Vault.IsOpen ? Vault.Note : ReadPlain(AppPaths.Note);
        bool fresh = false;

        if (Vault.IsDamaged)
        {
            Book = Notebook.Of(Welcome.Damaged());
            Say("Contenitore danneggiato: salvataggio sospeso.");
        }
        else if (Notebook.Load(stored) is { } saved)
        {
            Book = saved;
            Say(Vault.IsOpen ? "Nota aperta dalla cassaforte." : "Nota aperta.");
        }
        else
        {
            Book = Notebook.Of(Welcome.Note());
            fresh = true;
            Say(stored is { Length: > 0 } ? "La nota salvata non si legge: ne apro una nuova (la vecchia resta in note.xaml.bak)." : "Nota nuova.");

            if (stored is { Length: > 0 } && !Vault.IsOpen)
            {
                TryCopy(AppPaths.Note, AppPaths.NoteBackup);
            }
        }

        if (Vault.IsOpen)
        {
            Session.RestoreHidden(HostList.Parse(Vault.Hidden));
        }

        Session.Restore(LastScanFile.Read(Vault.IsOpen ? Vault.LastScan : ReadText(AppPaths.LastScan)));

        _dirty = fresh || Book.NeedsRewrite;
        _noteHosts = null;
        CurrentSheet = null;
        NoteReplaced?.Invoke();
        SyncHosts();

        if (_dirty)
        {
            SaveNote();
        }
    }

    /// <summary>Il foglio e' cambiato nell'editor: si tiene il documento nuovo e si salva fra poco.</summary>
    public void Update(Sheet sheet, SheetDoc doc)
    {
        sheet.Replace(doc);
        Touch();
    }

    /// <summary>Qualcosa nella nota e' cambiato: si salva fra poco, a meno che non si continui a scrivere.</summary>
    public void Touch()
    {
        if (!_dirty)
        {
            _dirty = true;
            Unsaved?.Invoke();
        }

        _noteHosts = null;
        _save.Interval = TimeSpan.FromMilliseconds(Settings.AutosaveEnabled ? Settings.AutosaveDebounceMs : 4000);

        if (Settings.AutosaveEnabled)
        {
            _save.Stop();
            _save.Start();
        }
    }

    /// <summary>
    /// Salva la nota. I fogli diventano XAML qui (solo quelli cambiati: gli altri riusano lo XAML
    /// che avevano); comprimere, cifrare e scrivere vanno nel thread delle scritture.
    /// </summary>
    public bool SaveNote(bool force = false, bool wait = false)
    {
        if ((!_dirty && !force) || IsLocked || Vault.IsDamaged)
        {
            if (wait)
            {
                _io.Drain();
            }

            return true;
        }

        bool sealedNote = VaultSession.Exists;

        if (sealedNote && !Vault.IsOpen)
        {
            return false;
        }

        NoteArchive photo = Book.Snapshot();
        _dirty = false;
        bool backup = !sealedNote && DateTime.UtcNow - _lastBackup >= BackupEvery;

        if (backup)
        {
            _lastBackup = DateTime.UtcNow;
        }

        _io.Post("nota", () =>
        {
            byte[] note = photo.Save();

            if (sealedNote)
            {
                Vault.WriteNote(note);
            }
            else
            {
                AppPaths.Ensure();

                if (backup && File.Exists(AppPaths.Note))
                {
                    File.Copy(AppPaths.Note, AppPaths.NoteBackup, overwrite: true);
                }

                string temp = AppPaths.Note + ".tmp";
                File.WriteAllBytes(temp, note);
                File.Move(temp, AppPaths.Note, overwrite: true);
            }

            Dispatcher.UIThread.Post(() =>
            {
                LastSaved = DateTime.Now;
                Saved?.Invoke();
            });
        });

        if (wait)
        {
            _io.Drain();
        }

        return true;
    }

    private byte[] Snapshot()
    {
        _io.Drain();
        return Book.Save();
    }

    // ------------------------------------------------------------------ host della nota

    private List<string>? _noteHosts;

    /// <summary>Gli indirizzi scritti in tutti i fogli (non nel codice), senza quelli messi da parte.</summary>
    public IReadOnlyList<string> NoteHosts()
    {
        _noteHosts ??= Written();
        return [.. _noteHosts.Where(host => !Session.IsHidden(host))];
    }

    /// <summary>Quanti indirizzi ci sono nella nota (per la scheda del monitoraggio).</summary>
    public int NoteHostCount => NoteHosts().Count;

    private List<string> Written()
    {
        List<string> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Sheet sheet in Book.Sheets)
        {
            foreach (SheetLine line in sheet.Doc.Lines())
            {
                if (line.Kind == LineKind.Code)
                {
                    continue;
                }

                foreach (string row in line.Text.Split('\n'))
                {
                    foreach (HostToken token in HostTokens.Find(row))
                    {
                        if (seen.Add(token.Value))
                        {
                            hosts.Add(token.Value);
                        }
                    }
                }
            }
        }

        return hosts;
    }

    /// <summary>
    /// Allinea la tabella alla nota: i nuovi indirizzi si controllano appena ci si ferma. Quello
    /// che si sta ancora scrivendo (<paramref name="typing"/>) aspetta: "nb.zaick" sulla strada
    /// di "nb.zaick.net" non e' un host. Per togliere una riga invece non si aspetta.
    /// </summary>
    public void SyncHosts(string? typing = null)
    {
        IReadOnlyList<string> written = NoteHosts();
        Session.Prune(written);

        IEnumerable<string> settled = typing is null
            ? written
            : written.Where(host => !host.Equals(typing, StringComparison.OrdinalIgnoreCase));

        if (Session.EnsurePending(settled).Count > 0)
        {
            _fresh.Stop();
            _fresh.Start();
        }

        RaiseHosts();
    }

    /// <summary>Lo stato di un indirizzo: true raggiungibile, false spento, null mai provato.</summary>
    public bool? StateOf(string host) => Session.Find(host) is { NeedsCheck: false } row ? row.IsUp : null;

    /// <summary>Lo stato di tutti gli indirizzi della nota, come lo vuole l'editor: up, down, unknown.</summary>
    public Dictionary<string, string> HostStates()
    {
        Dictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);

        foreach (string host in Written())
        {
            states[host.ToLowerInvariant()] = StateOf(host) switch
            {
                true => "up",
                false => "down",
                _ => "unknown"
            };
        }

        return states;
    }

    /// <summary>L'ultima analisi l'ha fatta partire DuckNote da solo: la fine non merita una notifica.</summary>
    public bool ScanIsAutomatic { get; private set; }

    public async Task Scan(string targets, bool keep, bool automatic = false, bool force = false)
    {
        ScanIsAutomatic = automatic;
        await Session.RunAsync(targets, Settings.ToScanOptions(), keep, force);
    }

    /// <summary>Un intervallo scritto nella pagina Rete: si ricorda per la prossima volta.</summary>
    public async Task ScanRange(string range)
    {
        Settings.LastRange = range;
        Settings.Save();
        await Scan(range, keep: false);
    }

    /// <summary>"Host della nota": nell'elenco restano solo loro, e si controllano adesso.</summary>
    public async Task ScanNote()
    {
        IReadOnlyList<string> hosts = NoteHosts();

        if (!Session.IsRunning && Session.KeepOnlyNote() > 0)
        {
            RaiseHosts();
        }

        if (hosts.Count == 0)
        {
            Say("Nella nota non ci sono indirizzi da analizzare.");
            return;
        }

        await Scan(string.Join(',', hosts), keep: true);
    }

    public async Task Analyse(string host)
    {
        if (Session.Show(host))
        {
            KeepHidden();
        }

        Session.EnsurePending([host]);
        await Scan(host, keep: true, force: true);
    }

    public void Hide(string host)
    {
        if (Session.Hide(host))
        {
            KeepHidden();
            Say(Session.Message);
            RaiseHosts();
        }
    }

    public void Show(string host)
    {
        if (Session.Show(host))
        {
            KeepHidden();
            Say(Session.Message);
            SyncHosts();
        }
    }

    private void KeepHidden()
    {
        string list = HostList.Write(Session.Hidden);

        if (Vault.IsOpen)
        {
            _io.Post("da parte", () =>
            {
                Vault.Hidden = list;
                Vault.Save();
            });

            if (Settings.IgnoredHosts.Length > 0)
            {
                Settings.IgnoredHosts = string.Empty;
                Settings.Save();
            }

            return;
        }

        Settings.IgnoredHosts = list;
        Settings.Save();
    }

    private void KeepLastScan()
    {
        List<IReadOnlyDictionary<string, string>> records = [.. Session.Records()];
        bool open = Vault.IsOpen;

        if (!open && VaultSession.Exists)
        {
            return;
        }

        _io.Post("scansione", () =>
        {
            string json = LastScanFile.Write(records);

            if (open)
            {
                Vault.LastScan = json;
                Vault.Save();
                return;
            }

            try
            {
                AppPaths.Ensure();
                File.WriteAllText(AppPaths.LastScan, json);
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
            {
                // l'ultima scansione e' una comodita': se non si scrive, si rifa'
            }
        });
    }

    // ------------------------------------------------------------------ cassaforte

    public void Encrypt(byte[] key, DuckNote.Core.Crypto.KdfParameters kdf, byte[] salt)
    {
        SaveNote(wait: true);
        Vault.Create(key, kdf, salt, Snapshot(), LastScanFile.Write(Session.Records()), HostList.Write(Session.Hidden));
        Settings.IgnoredHosts = string.Empty;
        Settings.SecurityPrompted = true;
        Settings.Save();
        _dirty = false;
        VaultChanged?.Invoke();
    }

    public bool Unlock(byte[] key)
    {
        _io.Drain();

        if (!Vault.Unlock(key))
        {
            return false;
        }

        IsLocked = false;
        _lastActivity = DateTime.UtcNow;
        OpenNote();
        StartTimers();
        VaultChanged?.Invoke();

        return true;
    }

    public void Lock(string reason)
    {
        if (!Vault.IsOpen)
        {
            return;
        }

        SaveNote(wait: true);
        Session.Stop();
        StopTimers();
        _io.Drain();
        Vault.Lock();

        Book.Forget();
        Book = Notebook.Of(SheetDoc.Blank());
        Session.Clear();
        IsLocked = true;
        _noteHosts = null;
        CurrentSheet = null;

        NoteReplaced?.Invoke();
        VaultChanged?.Invoke();
        Say($"Chiusa a chiave ({reason}).");
        Locked?.Invoke();
    }

    public bool Decrypt()
    {
        SaveNote(force: true, wait: true);

        if (!Vault.Disable(Snapshot()))
        {
            return false;
        }

        Settings.IgnoredHosts = HostList.Write(Session.Hidden);
        Settings.Save();
        KeepLastScan();
        VaultChanged?.Invoke();

        return true;
    }

    public void ChangePassword(byte[] key, DuckNote.Core.Crypto.KdfParameters kdf, byte[] salt)
    {
        _io.Drain();
        Vault.ChangePassword(key, kdf, salt);
    }

    // ------------------------------------------------------------------ orologi

    public void StartTimers()
    {
        _monitor.Stop();
        _monitor.Interval = TimeSpan.FromSeconds(Math.Max(10, Settings.MonitorIntervalSec));

        if (Settings.MonitorEnabled && !IsLocked)
        {
            _monitor.Start();
        }

        _idle.Stop();

        if (Settings.AutoLockMinutes > 0)
        {
            _idle.Start();
        }
    }

    public void StopTimers()
    {
        _save.Stop();
        _fresh.Stop();
        _monitor.Stop();
        _idle.Stop();
    }

    /// <summary>Cancella ogni dato: da qui in poi niente si salva piu', nemmeno alla chiusura.</summary>
    public void Wipe()
    {
        _wiped = true;
        StopTimers();
        Session.Stop();
        _io.Drain();
        Vault.Dispose();
        AppWipe.Remove();
    }

    public void Shutdown()
    {
        if (_wiped)
        {
            return;
        }

        StopTimers();
        Session.Stop();
        SaveNote(wait: true);
        _io.Drain();
        Vault.Dispose();
        Settings.Save();
    }

    private static byte[]? ReadPlain(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadText(string path) =>
        ReadPlain(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : string.Empty;

    private static void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // niente copia: la nota nuova non sovrascrive comunque nulla finche' non si scrive
        }
    }
}
