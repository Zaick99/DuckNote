using System.IO;
using System.Windows.Documents;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Scanning;
using DuckNote.App.Vault;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Scanning;
using DuckNote.Notes.Settings;

namespace DuckNote.App;

/// <summary>
/// Lo stato dell'applicazione, condiviso dalle pagine: impostazioni, nota, cassaforte, tabella
/// degli host. Le pagine mostrano e chiedono; qui si decide cosa si salva, cosa si analizza e
/// quando.
/// </summary>
public sealed class Workspace
{
    private static readonly TimeSpan BackupEvery = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _fresh = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _monitor = new();
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(20) };

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

            if (Session.IsRunning || IsLocked)
            {
                return;
            }

            IReadOnlyList<string> waiting = Session.NeverChecked(NoteHosts(settled: true));

            if (waiting.Count > 0)
            {
                await Scan(string.Join(',', waiting), keep: true, automatic: true);
            }
        };

        _monitor.Tick += async (_, _) =>
        {
            if (Session.IsRunning || IsLocked || NoteHosts() is not { Count: > 0 } hosts)
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
            HostsChanged?.Invoke();
        };

        Session.Rows.CollectionChanged += (_, _) => HostsChanged?.Invoke();
    }

    public AppSettings Settings { get; }

    public VaultSession Vault { get; } = new();

    public ScanSession Session { get; } = new();

    public Book Book { get; private set; } = Book.Of(Doc.Empty());

    /// <summary>La nota e' chiusa a chiave: la cassaforte esiste ma la chiave non e' in memoria.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// Si sta fotografando la nota per scriverla: i riquadri escono dal documento e ci tornano.
    /// Per WPF sono modifiche come le altre; chi ascolta il testo non deve contarle come lavoro,
    /// o ogni salvataggio ne chiederebbe un altro, per sempre.
    /// </summary>
    public bool IsSaving { get; private set; }

    /// <summary>Quando la nota e' stata scritta l'ultima volta (null finche' non succede).</summary>
    public DateTime? LastSaved { get; private set; }

    /// <summary>Il contenitore non ha superato la verifica: si mostra un avviso e non si salva nulla.</summary>
    public bool IsDamaged => Vault.IsDamaged;

    /// <summary>Dove sta il cursore nell'editor (riga e colonna): l'indirizzo che sta scrivendo non conta ancora.</summary>
    public Func<(Paragraph? Row, int Column)>? Caret { get; set; }

    public event Action<string>? Status;

    /// <summary>La nota e' appena stata scritta.</summary>
    public event Action? Saved;

    /// <summary>Gli host della tabella o il loro stato sono cambiati.</summary>
    public event Action? HostsChanged;

    /// <summary>La nota e' stata sostituita per intero (apertura, sblocco, blocco).</summary>
    public event Action? NoteReplaced;

    /// <summary>La cassaforte e' cambiata (creata, aperta, chiusa, tolta).</summary>
    public event Action? VaultChanged;

    /// <summary>Si e' chiusa a chiave: la finestra deve coprirsi.</summary>
    public event Action? Locked;

    /// <summary>Qualcuno vuole vedere la scheda di un host (dalla nota, dall'elenco host).</summary>
    public event Action<string>? HostRequested;

    /// <summary>Si chiede di portare del contenuto in nota (una tabella di risultati).</summary>
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
        byte[]? stored = Vault.IsOpen ? Vault.Note : ReadPlain(AppPaths.Note);
        bool fresh = false;

        if (Vault.IsDamaged)
        {
            Book = Book.Of(Welcome.Damaged());
            Say("Contenitore danneggiato: salvataggio sospeso.");
        }
        else if (Book.Load(stored) is { } saved)
        {
            Book = saved;
            Say(Vault.IsOpen ? "Nota aperta dalla cassaforte." : "Nota aperta.");
        }
        else
        {
            Book = Book.Of(Welcome.Note());
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
        NoteReplaced?.Invoke();
        SyncHosts();

        if (_dirty)
        {
            SaveNote();
        }
    }

    /// <summary>Qualcosa nella nota e' cambiato: si salva fra poco, a meno che non si continui a scrivere.</summary>
    public void Touch()
    {
        _dirty = true;
        _save.Interval = TimeSpan.FromMilliseconds(Settings.AutosaveEnabled ? Settings.AutosaveDebounceMs : 4000);

        if (Settings.AutosaveEnabled)
        {
            _save.Stop();
            _save.Start();
        }
    }

    public bool SaveNote(bool force = false)
    {
        if ((!_dirty && !force) || IsLocked || Vault.IsDamaged)
        {
            return true;
        }

        try
        {
            byte[] note = Snapshot();

            if (VaultSession.Exists)
            {
                if (!Vault.IsOpen)
                {
                    return false;
                }

                Vault.WriteNote(note);
            }
            else
            {
                AppPaths.Ensure();

                // la copia di prima, rinfrescata al massimo ogni quarto d'ora: non a ogni tasto
                if (File.Exists(AppPaths.Note) && DateTime.UtcNow - _lastBackup >= BackupEvery)
                {
                    File.Copy(AppPaths.Note, AppPaths.NoteBackup, overwrite: true);
                    _lastBackup = DateTime.UtcNow;
                }

                string temp = AppPaths.Note + ".tmp";
                File.WriteAllBytes(temp, note);
                File.Move(temp, AppPaths.Note, overwrite: true);
            }

            _dirty = false;
            LastSaved = DateTime.Now;
            Saved?.Invoke();
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            Say("Nota non salvata: la cartella non e' scrivibile.");
            return false;
        }
    }

    private byte[] Snapshot()
    {
        IsSaving = true;

        try
        {
            return Book.Save();
        }
        finally
        {
            IsSaving = false;
        }
    }

    // ------------------------------------------------------------------ host della nota

    /// <summary>
    /// Gli indirizzi scritti in tutte le pagine. Quello che il cursore sta ancora scrivendo non
    /// conta, a meno che la riga non si consideri finita (<paramref name="settled"/>).
    /// </summary>
    public IReadOnlyList<string> NoteHosts(bool settled = false)
    {
        (Paragraph? writing, int caret) = settled || Caret is null ? (null, -1) : Caret();
        List<string> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Sheet sheet in Book.Sheets)
        {
            foreach (Paragraph row in Doc.Rows(sheet.Document))
            {
                if (Looks.Is(row, Look.Code))
                {
                    continue;
                }

                bool here = ReferenceEquals(row, writing);

                foreach (HostToken token in HostTokens.Find(Doc.LineOf(row)))
                {
                    if (here && caret >= 0 && HostTokens.Touches(token, caret))
                    {
                        continue;
                    }

                    if (seen.Add(token.Value) && !Session.IsHidden(token.Value))
                    {
                        hosts.Add(token.Value);
                    }
                }
            }
        }

        return hosts;
    }

    /// <summary>
    /// Allinea la tabella alla nota. Per aggiungere una riga si aspetta la parola finita; per
    /// toglierla no: quel che e' scritto e' scritto, anche col cursore addosso.
    /// </summary>
    public void SyncHosts(bool settled = false)
    {
        IReadOnlyList<string> written = NoteHosts(settled: true);
        Session.Prune(written);

        if (Session.EnsurePending(settled ? written : NoteHosts()).Count > 0)
        {
            // un indirizzo appena scritto si controlla da solo, appena si smette di scrivere
            _fresh.Stop();
            _fresh.Start();
        }

        HostsChanged?.Invoke();
    }

    /// <summary>Lo stato di un indirizzo: true raggiungibile, false spento, null mai provato.</summary>
    public bool? StateOf(string host) => Session.Find(host) is { IsChecked: true } row ? row.IsUp : null;

    /// <summary>
    /// L'ultima analisi l'ha fatta partire DuckNote da solo (monitoraggio, indirizzi appena
    /// scritti) e non chi la usa: la fine non merita una notifica.
    /// </summary>
    public bool ScanIsAutomatic { get; private set; }

    public async Task Scan(string targets, bool keep, bool automatic = false, bool force = false)
    {
        ScanIsAutomatic = automatic;
        await Session.RunAsync(targets, Settings.ToScanOptions(), keep, force);
        SyncHosts();
    }

    public async Task ScanRange(string range)
    {
        Settings.LastRange = range;
        Settings.Save();
        await Scan(range, keep: false);
    }

    public async Task ScanNote()
    {
        IReadOnlyList<string> hosts = NoteHosts();

        // "Host della nota": nell'elenco restano solo loro, gli altri trovati per intervallo escono
        if (!Session.IsRunning && Session.KeepOnlyNote() > 0)
        {
            HostsChanged?.Invoke();
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
            HostsChanged?.Invoke();
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
            Vault.Hidden = list;
            Vault.Save();

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
        string json = LastScanFile.Write(Session.Records());

        if (Vault.IsOpen)
        {
            Vault.LastScan = json;
            Vault.Save();
            return;
        }

        if (VaultSession.Exists)
        {
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
    }

    // ------------------------------------------------------------------ cassaforte

    public void Encrypt(byte[] key, DuckNote.Core.Crypto.KdfParameters kdf, byte[] salt)
    {
        SaveNote();
        Vault.Create(key, kdf, salt, Snapshot(), LastScanFile.Write(Session.Records()), HostList.Write(Session.Hidden));
        Settings.IgnoredHosts = string.Empty;
        Settings.SecurityPrompted = true;
        Settings.Save();
        _dirty = false;
        VaultChanged?.Invoke();
    }

    public bool Unlock(byte[] key)
    {
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

        SaveNote();
        Session.Stop();
        StopTimers();
        Vault.Lock();

        Book.Forget();
        Book = Book.Of(Doc.Empty());
        Session.Clear();
        IsLocked = true;

        NoteReplaced?.Invoke();
        VaultChanged?.Invoke();
        Say($"Chiusa a chiave ({reason}).");
        Locked?.Invoke();
    }

    public bool Decrypt()
    {
        SaveNote(force: true);

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

    public void ChangePassword(byte[] key, DuckNote.Core.Crypto.KdfParameters kdf, byte[] salt) =>
        Vault.ChangePassword(key, kdf, salt);

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
        Vault.Dispose();
        Storage.AppWipe.Remove();
    }

    public void Shutdown()
    {
        if (_wiped)
        {
            return;
        }

        StopTimers();
        Session.Stop();
        SaveNote();
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
