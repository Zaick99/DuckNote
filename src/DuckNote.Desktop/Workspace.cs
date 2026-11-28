using Avalonia.Threading;
using DuckNote.App.Scanning;
using DuckNote.App.Storage;
using DuckNote.App.Vault;
using System.Text.Json.Nodes;
using DuckNote.Desktop.Notes;
using DuckNote.Notes.Calendar;
using DuckNote.Notes.Groups;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Maps;
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

    /// <summary>L'orologio dei promemoria del calendario: batte ogni 20 secondi.</summary>
    private readonly DispatcherTimer _calendarTick = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly ReminderClock _reminders = new();

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

            IReadOnlyList<string> waiting = Session.NeverChecked(Watched());

            if (waiting.Count > 0)
            {
                await Scan(string.Join(',', waiting), keep: true, automatic: true);
            }
        };

        _monitor.Tick += async (_, _) =>
        {
            // gli indirizzi gia' in tabella: non la parola che si sta ancora scrivendo; con loro
            // gli host dei gruppi col monitoraggio acceso
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
        Touch();
        SyncHosts();
    }

    /// <summary>Le mappe della nota per la pagina della mappa (null: la nota non ne ha).</summary>
    public string? MapJson => Book.Map is { Length: > 0 } map ? System.Text.Encoding.UTF8.GetString(map) : null;

    // gli indirizzi degli apparati delle mappe, letti di nuovo solo quando le mappe cambiano
    private byte[]? _mapHostsOf;
    private IReadOnlyList<string> _mapHosts = [];

    /// <summary>Come compare l'host su una mappa della nota (l'indirizzo dell'apparato), o null se non c'e'.</summary>
    public string? MapHostOf(HostRow row)
    {
        if (IsLocked || Book.Map is null)
        {
            return null;
        }

        if (!ReferenceEquals(_mapHostsOf, Book.Map))
        {
            _mapHosts = NetworkMap.Hosts(Book.Map);
            _mapHostsOf = Book.Map;
        }

        HashSet<string> names = new(row.Names(), StringComparer.OrdinalIgnoreCase);
        return _mapHosts.FirstOrDefault(names.Contains);
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

    /// <summary>Gli indirizzi scritti in tutti i fogli (non nel codice) e quelli degli apparati della mappa, senza quelli messi da parte.</summary>
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

        // gli apparati della mappa si controllano come gli indirizzi scritti
        foreach (string host in NetworkMap.Hosts(Book.Map))
        {
            if (seen.Add(host))
            {
                hosts.Add(host);
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
        IReadOnlyList<string> written = Watched();
        Session.Prune(written, Groups.InAny);

        IEnumerable<string> settled = typing is null
            ? written
            : written.Where(host => !host.Equals(typing, StringComparison.OrdinalIgnoreCase));

        if (Session.EnsurePending(settled).Count > 0)
        {
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

    /// <summary>Il monitoraggio della Nota e della Ricerca, scritto nella nota coi gruppi.</summary>
    public ScanGroupFlags GroupFlags => ScanGroups.ReadFlags(Book.Groups);

    /// <summary>Il monitoraggio e' cambiato (un interruttore dei gruppi, o quello di tutto).</summary>
    public event Action? MonitorChanged;

    /// <summary>I gruppi nuovi, nella nota (e quindi nella cassaforte): si salvano fra poco.</summary>
    public void SaveGroups(IReadOnlyList<ScanGroup> groups) => SaveGroups(groups, GroupFlags);

    /// <summary>I gruppi e il monitoraggio di Nota e Ricerca, nella nota.</summary>
    public void SaveGroups(IReadOnlyList<ScanGroup> groups, ScanGroupFlags flags)
    {
        if (IsLocked || IsDamaged)
        {
            return;
        }

        Book.Groups = ScanGroups.Write(groups, flags);
        _groupHosts = null;
        Touch();
        SyncHosts();
        GroupsChanged?.Invoke();
    }

    /// <summary>
    /// L'interruttore di un gruppo del pannello di Rete: Tutti accende o spegne il monitoraggio
    /// intero (un'impostazione dell'app), Nota e Ricerca si scrivono nella nota, un gruppo proprio
    /// cambia il suo "Monitora".
    /// </summary>
    public void SetMonitor(string id, bool on)
    {
        if (IsLocked || IsDamaged)
        {
            return;
        }

        switch (id)
        {
            case GroupViews.All:
                Settings.MonitorEnabled = on;
                Settings.Save();
                StartTimers();
                break;
            case ScanGroups.Note:
                SaveGroups([.. Groups.All], GroupFlags with { NoteMonitor = on });
                break;
            case GroupViews.Found:
                SaveGroups([.. Groups.All], GroupFlags with { FoundMonitor = on });
                break;
            default:
                if (Groups.Find(id) is null)
                {
                    return;
                }

                SaveGroups([.. Groups.All.Select(group => group.Id == id ? group with { Monitor = on } : group)]);
                break;
        }

        MonitorChanged?.Invoke();
    }

    /// <summary>
    /// Gli indirizzi che stanno in tabella anche senza un'analisi e si controllano da soli: quelli
    /// della nota (fogli e mappa) e quelli singoli dei gruppi col monitoraggio acceso.
    /// </summary>
    public IReadOnlyList<string> Watched()
    {
        IReadOnlyList<string> note = NoteHosts();
        HashSet<string> seen = new(note, StringComparer.OrdinalIgnoreCase);
        return [.. note, .. Groups.WatchedSingles().Where(host => !Session.IsHidden(host) && seen.Add(host))];
    }

    /// <summary>Quello che il monitoraggio controlla: gli indirizzi tenuti in tabella e gli host gia' trovati nelle reti dei gruppi monitorati.</summary>
    public IReadOnlyList<string> Monitored()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        ScanGroupFlags flags = GroupFlags;
        IEnumerable<string> note = flags.NoteMonitor ? NoteHosts().Where(host => !Session.IsHidden(host)) : [];
        IEnumerable<string> found = flags.FoundMonitor
            ? Session.Rows.Where(row => !row.FromNote && !Groups.InAny(row) && row.Address.Length > 0 && !Session.IsHidden(row.Address)).Select(row => row.Address)
            : [];
        return [.. note.Concat(Groups.WatchedSingles().Where(host => !Session.IsHidden(host)))
            .Where(host => Session.Find(host) is not null)
            .Concat(Groups.MonitoredRows(Session.Rows).Where(host => !Session.IsHidden(host)))
            .Concat(found)
            .Where(seen.Add)];
    }

    /// <summary>Quanti host controlla il monitoraggio (per la scheda in fondo alla sidebar).</summary>
    public int MonitoredCount => Monitored().Count;

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
        await Session.RunAsync(targets, Settings.ToScanOptions(), keep, force, Groups.InAny);
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
