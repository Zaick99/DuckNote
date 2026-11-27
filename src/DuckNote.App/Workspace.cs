using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Documents;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Scanning;
using DuckNote.App.Vault;
using DuckNote.Notes.Calendar;
using DuckNote.Notes.Groups;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Maps;
using DuckNote.Notes.Notebook;
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

    /// <summary>L'orologio dei promemoria del calendario: batte ogni 20 secondi.</summary>
    private readonly DispatcherTimer _calendarTick = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly ReminderClock _reminders = new();

    /// <summary>Raggruppa gli avvisi "gli host sono cambiati": durante un'analisi ne arrivano a decine al secondo.</summary>
    private readonly DispatcherTimer _hosts = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };

    /// <summary>Le scritture su disco, fuori dal thread dell'interfaccia.</summary>
    private readonly Storage.IoQueue _io = new();

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

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
                // i nuovi aspettano che finisca, non il giro dopo del monitoraggio. Prima si
                // perdevano: tre nomi scritti di fila, e solo il primo veniva controllato.
                _fresh.Start();
                return;
            }

            IReadOnlyList<string> waiting = Session.NeverChecked(Watched(settled: true));

            if (waiting.Count > 0)
            {
                await Scan(string.Join(',', waiting), keep: true, automatic: true);
            }
        };

        _monitor.Tick += async (_, _) =>
        {
            // tutti gli indirizzi gia' in tabella, anche l'ultimo scritto se il cursore e'
            // rimasto subito dopo: e' entrato in tabella quando la parola si e' fermata. Fuori
            // resta solo una parola ancora a meta', che in tabella non c'e'.
            if (Session.IsRunning || IsLocked || Monitored() is not { Count: > 0 } hosts)
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
            LearnCertificates();
            RaiseHosts();
        };

        _calendarTick.Tick += (_, _) =>
        {
            if (IsLocked || IsDamaged)
            {
                return;
            }

            IReadOnlyList<AgendaAlert> due = _reminders.Tick(Calendar, DateTime.Now);
            CalendarTick?.Invoke();

            if (due.Count > 0)
            {
                RemindersDue?.Invoke(due);
            }
        };

        Session.Rows.CollectionChanged += (_, _) => RaiseHosts();
        Session.PropertyChanged += (_, _) => RaiseHosts();

        _hosts.Tick += (_, _) =>
        {
            _hosts.Stop();
            HostsChanged?.Invoke();
        };

        _io.Failed += (what, problem) => _ui.BeginInvoke(() =>
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

    /// <summary>
    /// Avvisa che gli host sono cambiati, ma non a ogni singolo risultato: il primo avviso
    /// fa partire un breve conto e tutti quelli che arrivano nel frattempo valgono come uno.
    /// </summary>
    private void RaiseHosts()
    {
        if (!_hosts.IsEnabled)
        {
            _hosts.Start();
        }
    }

    /// <summary>Il foglio aperto nell'editor: e' quello che cambia quando si tocca la nota.</summary>
    public Sheet? CurrentSheet { get; set; }

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

    /// <summary>Nella nota c'e' qualcosa che non e' ancora su disco (la prima modifica dopo un salvataggio).</summary>
    public event Action? Unsaved;

    /// <summary>Ci sono modifiche che aspettano di essere scritte.</summary>
    public bool HasUnsaved => _dirty;

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
        _io.Drain();
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
        _noteHosts = null;
        _mapHosts = null;
        _groupHosts = null;
        _agenda = null;
        CurrentSheet = null;
        NoteReplaced?.Invoke();
        GroupsChanged?.Invoke();
        SyncHosts();
        LearnCertificates();

        if (_dirty)
        {
            SaveNote();
        }

        // i promemoria scattati mentre la nota era chiusa non suonano: li dice il riepilogo
        if (!IsDamaged)
        {
            _reminders.Start(DateTime.Now);
            CalendarChanged?.Invoke();
            CalendarOpened?.Invoke(Calendar.Summary(DateTime.Now));
        }
        else
        {
            _reminders.Stop();
        }
    }

    /// <summary>Qualcosa nella nota e' cambiato: si salva fra poco, a meno che non si continui a scrivere.</summary>
    public void Touch()
    {
        if (!_dirty)
        {
            _dirty = true;
            Unsaved?.Invoke();
        }

        CurrentSheet?.Changed();
        _noteHosts = null;
        _save.Interval = TimeSpan.FromMilliseconds(Settings.AutosaveEnabled ? Settings.AutosaveDebounceMs : 4000);

        if (Settings.AutosaveEnabled)
        {
            _save.Stop();
            _save.Start();
        }
    }

    /// <summary>
    /// Salva la nota. La fotografia dei fogli si fa qui (e solo di quelli cambiati); comprimere,
    /// cifrare e scrivere vanno nel thread delle scritture, cosi' chi scrive non si ferma mai.
    /// <paramref name="wait"/> aspetta che la scrittura sia finita: serve solo a chi sta per
    /// chiudere o bloccare.
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

        NoteArchive photo;
        IsSaving = true;

        try
        {
            photo = Book.Snapshot();
        }
        finally
        {
            IsSaving = false;
        }

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

                // la copia di prima, rinfrescata al massimo ogni quarto d'ora: non a ogni tasto
                if (backup && File.Exists(AppPaths.Note))
                {
                    File.Copy(AppPaths.Note, AppPaths.NoteBackup, overwrite: true);
                }

                string temp = AppPaths.Note + ".tmp";
                File.WriteAllBytes(temp, note);
                File.Move(temp, AppPaths.Note, overwrite: true);
            }

            _ui.BeginInvoke(() =>
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

    /// <summary>La nota in byte, subito: per cifrare, togliere la cifratura.</summary>
    private byte[] Snapshot()
    {
        _io.Drain();
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

        foreach ((Paragraph row, HostToken token) in Written())
        {
            if (caret >= 0 && ReferenceEquals(row, writing) && HostTokens.Touches(token, caret))
            {
                continue;
            }

            if (seen.Add(token.Value) && !Session.IsHidden(token.Value))
            {
                hosts.Add(token.Value);
            }
        }

        // gli apparati della mappa si controllano come gli indirizzi scritti
        foreach (string host in MapHosts())
        {
            if (seen.Add(host) && !Session.IsHidden(host))
            {
                hosts.Add(host);
            }
        }

        return hosts;
    }

    private IReadOnlyList<string>? _mapHosts;

    /// <summary>Gli indirizzi degli apparati della mappa (letti una volta per modifica della mappa).</summary>
    private IReadOnlyList<string> MapHosts() => _mapHosts ??= NetworkMap.Hosts(Book.Map);

    /// <summary>Come compare l'host su una mappa della nota (l'indirizzo dell'apparato), o null se non c'e'.</summary>
    public string? MapHostOf(HostRow row)
    {
        if (IsLocked || Book.Map is null)
        {
            return null;
        }

        HashSet<string> names = new(row.Names(), StringComparer.OrdinalIgnoreCase);
        return MapHosts().FirstOrDefault(names.Contains);
    }

    /// <summary>
    /// Le mappe della rete sono cambiate nella loro pagina: si tengono (se sono davvero un
    /// documento di mappe) e si salvano fra poco, come un foglio, dentro la nota e quindi nella
    /// cassaforte. Gli indirizzi degli apparati entrano fra quelli che DuckNote controlla.
    /// </summary>
    public void UpdateMap(string json)
    {
        if (IsLocked || IsDamaged || NetworkMap.Clean(json) is not { } clean)
        {
            return;
        }

        byte[]? next = NetworkMap.IsBlank(clean) ? null : clean;

        if (Book.Map is { } current ? next is not null && current.AsSpan().SequenceEqual(next) : next is null)
        {
            return;
        }

        Book.Map = next;
        _mapHosts = null;
        Touch();
        SyncHosts(settled: true);
    }

    /// <summary>Le mappe della nota per la pagina della mappa (null: la nota non ne ha).</summary>
    public string? MapJson => Book.Map is { Length: > 0 } map ? System.Text.Encoding.UTF8.GetString(map) : null;

    private List<(Paragraph Row, HostToken Token)>? _noteHosts;

    /// <summary>
    /// Quanti indirizzi c'erano l'ultima volta che si e' letta la nota. Per mostrare un numero
    /// (scheda del monitoraggio) senza rileggere tutto mentre si scrive.
    /// </summary>
    public int NoteHostCount { get; private set; }

    /// <summary>
    /// Tutti gli indirizzi scritti nei fogli, con la riga in cui stanno. Leggere l'intera nota
    /// costa: lo si fa una volta per modifica (<see cref="Touch"/> butta via il risultato), non
    /// a ogni domanda, e le domande sono tante (barra, contatori, tabella, monitoraggio).
    /// </summary>
    private List<(Paragraph Row, HostToken Token)> Written()
    {
        if (_noteHosts is { } kept)
        {
            return kept;
        }

        List<(Paragraph, HostToken)> found = [];

        foreach (Sheet sheet in Book.Sheets)
        {
            foreach (Paragraph row in Doc.Rows(sheet.Document))
            {
                if (Looks.Is(row, Look.Code))
                {
                    continue;
                }

                foreach (HostToken token in HostTokens.Find(Doc.LineOf(row)))
                {
                    found.Add((row, token));
                }
            }
        }

        NoteHostCount = found.Select(pair => pair.Item2.Value).Concat(MapHosts()).Distinct(StringComparer.OrdinalIgnoreCase).Count(host => !Session.IsHidden(host));

        return _noteHosts = found;
    }

    /// <summary>
    /// Allinea la tabella alla nota. Per aggiungere una riga si aspetta la parola finita; per
    /// toglierla no: quel che e' scritto e' scritto, anche col cursore addosso.
    /// </summary>
    public void SyncHosts(bool settled = false)
    {
        IReadOnlyList<string> written = Watched(settled: true);
        Session.Prune(written, Groups.InAny);

        if (Session.EnsurePending(settled ? written : Watched()).Count > 0)
        {
            // un indirizzo appena scritto si controlla da solo, appena si smette di scrivere
            _fresh.Stop();
            _fresh.Start();
        }

        Session.MarkWritten(NoteHosts());
        RaiseHosts();
    }

    // ------------------------------------------------------------------ gruppi di scansione

    private GroupHosts? _groupHosts;

    /// <summary>I gruppi di scansione della nota (oltre a "Nota", che c'e' sempre).</summary>
    public GroupHosts Groups => _groupHosts ??= new GroupHosts(ScanGroups.Read(Book.Groups));

    /// <summary>I gruppi sono cambiati (creati, modificati, tolti, o un'altra nota).</summary>
    public event Action? GroupsChanged;

    /// <summary>I gruppi nuovi, nella nota (e quindi nella cassaforte): si salvano fra poco.</summary>
    public void SaveGroups(IReadOnlyList<ScanGroup> groups)
    {
        if (IsLocked || IsDamaged)
        {
            return;
        }

        Book.Groups = ScanGroups.Write(groups);
        _groupHosts = null;
        Touch();
        SyncHosts(settled: true);
        GroupsChanged?.Invoke();
    }

    /// <summary>
    /// Gli indirizzi che stanno in tabella anche senza un'analisi e si controllano da soli: quelli
    /// della nota (fogli e mappa) e quelli singoli dei gruppi col monitoraggio acceso.
    /// </summary>
    public IReadOnlyList<string> Watched(bool settled = false)
    {
        IReadOnlyList<string> note = NoteHosts(settled);
        HashSet<string> seen = new(note, StringComparer.OrdinalIgnoreCase);
        return [.. note, .. Groups.WatchedSingles().Where(host => !Session.IsHidden(host) && seen.Add(host))];
    }

    /// <summary>Quello che il monitoraggio controlla: gli indirizzi tenuti in tabella e gli host gia' trovati nelle reti dei gruppi monitorati.</summary>
    public IReadOnlyList<string> Monitored()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        return [.. Watched(settled: true).Where(host => Session.Find(host) is not null)
            .Concat(Groups.MonitoredRows(Session.Rows).Where(host => !Session.IsHidden(host)))
            .Where(seen.Add)];
    }

    /// <summary>Quanti host controlla il monitoraggio (per la scheda in fondo alla sidebar).</summary>
    public int MonitoredCount => Monitored().Count;

    // ------------------------------------------------------------------ calendario

    private Agenda? _agenda;

    /// <summary>Il calendario della nota: le voci e i certificati visti dalle analisi.</summary>
    public Agenda Calendar => _agenda ??= Agenda.Read(Book.Calendar);

    /// <summary>Il calendario e' cambiato (una voce, un certificato, un'altra nota).</summary>
    public event Action? CalendarChanged;

    /// <summary>La nota si e' aperta: il riepilogo del calendario (anche vuoto).</summary>
    public event Action<AgendaSummary>? CalendarOpened;

    /// <summary>Sono scattati dei promemoria.</summary>
    public event Action<IReadOnlyList<AgendaAlert>>? RemindersDue;

    /// <summary>Un battito dell'orologio del calendario: il numero nella barra laterale cambia anche a mezzanotte.</summary>
    public event Action? CalendarTick;

    /// <summary>Un cambiamento dalla pagina del calendario: si applica e si salva fra poco.</summary>
    public bool ChangeCalendar(JsonObject change)
    {
        if (IsLocked || IsDamaged || !Calendar.Apply(change))
        {
            return false;
        }

        SaveCalendar();
        return true;
    }

    /// <summary>"Fatto" da un avviso: quella volta non suona piu'.</summary>
    public bool MarkDone(Occurrence occurrence)
    {
        if (IsLocked || IsDamaged || !Calendar.SetDone(occurrence.Item.Id, occurrence.Date, true))
        {
            return false;
        }

        SaveCalendar();
        return true;
    }

    /// <summary>"Rimanda" da un avviso: il promemoria torna a <paramref name="until"/>.</summary>
    public void Snooze(Occurrence occurrence, DateTime until) => _reminders.Snooze(occurrence, until);

    private void SaveCalendar()
    {
        Book.Calendar = Calendar.Write();
        Touch();
        CalendarChanged?.Invoke();
    }

    /// <summary>
    /// I certificati che le analisi hanno visto entrano nel calendario come scadenze: quelli che
    /// scadono da qui a tre anni (o sono scaduti da meno di un mese), uno per host.
    /// </summary>
    private void LearnCertificates()
    {
        if (IsLocked || IsDamaged)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        List<CertificateSeen> seen = [];

        foreach (HostRow row in Session.Rows)
        {
            if (row.Shown.Length == 0 || Agenda.Day(row.TlsExpiry) is not { } expires || expires < today.AddDays(-30) || expires > today.AddYears(3))
            {
                continue;
            }

            string name = row.Hostname.Equals(row.Shown, StringComparison.OrdinalIgnoreCase) ? string.Empty : row.Hostname;
            seen.Add(new CertificateSeen(row.Shown, expires, row.TlsSubject, row.TlsIssuer, name));
        }

        if (seen.Count > 0 && Calendar.Learn(seen))
        {
            SaveCalendar();
        }
    }

    /// <summary>"Analizza" su un gruppo: tutte le sue reti, i suoi intervalli e i suoi indirizzi; quello che c'era resta.</summary>
    public async Task ScanGroup(string id)
    {
        if (id == ScanGroups.Note)
        {
            await ScanNote();
            return;
        }

        if (Groups.Find(id) is not { } group || group.Targets.Count == 0)
        {
            Say("Il gruppo non ha bersagli: aggiungili con Modifica.");
            return;
        }

        await Scan(group.Expression, keep: true);
    }

    /// <summary>Lo stato di un indirizzo: true raggiungibile, false spento, null mai provato.</summary>
    public bool? StateOf(string host) => Session.Find(host) is { NeedsCheck: false } row ? row.IsUp : null;

    /// <summary>
    /// L'ultima analisi l'ha fatta partire DuckNote da solo (monitoraggio, indirizzi appena
    /// scritti) e non chi la usa: la fine non merita una notifica.
    /// </summary>
    public bool ScanIsAutomatic { get; private set; }

    public async Task Scan(string targets, bool keep, bool automatic = false, bool force = false)
    {
        ScanIsAutomatic = automatic;
        await Session.RunAsync(targets, Settings.ToScanOptions(), keep, force, Groups.InAny);
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
        if (!Session.IsRunning && Session.KeepOnlyNote(Groups.InAny) > 0)
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
        // i dati si leggono qui, la scrittura (e la cifratura di tutto il contenitore) altrove
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
        Book = Book.Of(Doc.Empty());
        Session.Clear();
        IsLocked = true;
        _noteHosts = null;
        _mapHosts = null;
        _groupHosts = null;
        _agenda = null;
        _reminders.Stop();
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

        _calendarTick.Start();
    }

    public void StopTimers()
    {
        _save.Stop();
        _fresh.Stop();
        _monitor.Stop();
        _idle.Stop();
        _calendarTick.Stop();
    }

    /// <summary>Cancella ogni dato: da qui in poi niente si salva piu', nemmeno alla chiusura.</summary>
    public void Wipe()
    {
        _wiped = true;
        StopTimers();
        Session.Stop();
        _io.Drain();
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
