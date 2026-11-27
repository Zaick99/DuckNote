using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DuckNote.App.Ui;
using DuckNote.Notes.Settings;
using DuckNote.Scan;

namespace DuckNote.App.Scanning;

/// <summary>
/// La tabella degli host e le scansioni che la riempiono. Gli host messi da parte non entrano
/// in nessuna scansione, ne' singola ne' per intervallo ne' del monitoraggio.
/// </summary>
public sealed class ScanSession : Observable
{
    private OuiVendorLookup _vendors = new(File.Exists(AppPaths.Oui) ? AppPaths.Oui : null);
    private readonly Dictionary<string, HostRow> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gli host gia' analizzati per intero da quando DuckNote e' aperto. I controlli dopo il
    /// primo sono solo un ping: nomi, MAC, porte e il resto restano quelli raccolti. Si
    /// ricomincia da capo solo al prossimo avvio (o quando un host torna acceso).
    /// </summary>
    private readonly HashSet<string> _full = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _stop;
    private int _done;
    private int _total;

    /// <summary>
    /// Gli host accesi che in questo giro non hanno risposto: restano com'erano finche' un
    /// secondo controllo, con calma, non dice che sono davvero spenti (vedi <see cref="Confirm"/>).
    /// </summary>
    private readonly List<Doubt> _doubts = [];

    private sealed record Doubt(string Target, HostRow Row, HostScanResult First);

    public ObservableCollection<HostRow> Rows { get; } = [];

    /// <summary>Quanti produttori conosce la tabella dei MAC (integrata, piu' oui.txt se c'e').</summary>
    public int Vendors => _vendors.Count;

    /// <summary>Rilegge la tabella dei produttori dopo che oui.txt e' stato aggiornato.</summary>
    public void ReloadVendors() => _vendors = new OuiVendorLookup(File.Exists(AppPaths.Oui) ? AppPaths.Oui : null);

    public IReadOnlyCollection<string> Hidden => _hidden;

    public bool IsRunning { get; private set; }

    public string Message { get; private set; } = "Pronto.";

    public double Progress => _total == 0 ? 0 : (double)_done / _total;

    public int UpCount => Rows.Count(row => row.IsUp);

    public int DownCount => Rows.Count(row => row.IsDown);

    public int WaitingCount => Rows.Count(row => row.NeedsCheck);

    /// <summary>Una scansione e' appena finita: il messaggio e' quello definitivo.</summary>
    public event Action<string>? Finished;

    public bool IsHidden(string host) => _hidden.Contains(host);

    public bool Hide(string host)
    {
        if (host.Length == 0 || !_hidden.Add(host))
        {
            return false;
        }

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Names().Any(_hidden.Contains))
            {
                Drop(i);
            }
        }

        Say($"{host} non verra' piu' analizzato.");

        return true;
    }

    public bool Show(string host)
    {
        if (!_hidden.Remove(host))
        {
            return false;
        }

        Say($"{host} torna fra gli host da analizzare.");

        return true;
    }

    public void RestoreHidden(IEnumerable<string> hosts)
    {
        _hidden.Clear();
        _hidden.UnionWith(hosts.Where(host => host.Length > 0));
    }

    /// <summary>
    /// La riga di un host, per uno qualsiasi dei suoi nomi (IP, com'e' scritto nella nota, nome
    /// DNS). Solo dall'indice: cercare riga per riga a ogni risultato fermerebbe l'interfaccia
    /// sulle reti grandi.
    /// </summary>
    public HostRow? Find(string host) => _index.GetValueOrDefault(host);

    /// <summary>
    /// Restano solo gli host scritti nella nota (e quelli che <paramref name="alsoKeep"/> vuole
    /// tenere: gli host dei gruppi): quelli trovati per intervallo escono.
    /// </summary>
    public int KeepOnlyNote(Func<HostRow, bool>? alsoKeep = null)
    {
        int dropped = 0;

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].NoteKey.Length == 0 && alsoKeep?.Invoke(Rows[i]) != true)
            {
                Drop(i);
                dropped++;
            }
        }

        if (dropped > 0)
        {
            RaiseAll();
        }

        return dropped;
    }

    /// <summary>
    /// Allinea la tabella alla nota: una riga nata da un indirizzo scritto lo segue, cancellato
    /// l'indirizzo se ne va. Una riga nata da un intervallo resta, se ha un esito.
    /// </summary>
    /// <param name="alsoKeep">Le righe gia' controllate che restano anche quando il loro
    /// indirizzo non e' piu' tenuto d'occhio: quelle dei gruppi. Restano sciolte, come se le
    /// avesse trovate un'analisi per intervallo.</param>
    public void Prune(IEnumerable<string> written, Func<HostRow, bool>? alsoKeep = null)
    {
        HashSet<string> keep = new(written, StringComparer.OrdinalIgnoreCase);

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            HostRow row = Rows[i];
            bool stays = row.NoteKey.Length > 0 ? keep.Contains(row.NoteKey) : row.IsChecked;

            if (stays)
            {
                continue;
            }

            if (row.NoteKey.Length > 0 && row.IsChecked && alsoKeep?.Invoke(row) == true)
            {
                Unbind(row);
                continue;
            }

            Drop(i);
        }
    }

    /// <summary>
    /// Segna quali righe tenute d'occhio sono scritte nella nota e quali ci stanno solo per un
    /// gruppo monitorato: solo le prime hanno la matita e passano dal filtro "Nota".
    /// </summary>
    public void MarkWritten(IEnumerable<string> note)
    {
        HashSet<string> written = new(note, StringComparer.OrdinalIgnoreCase);

        foreach (HostRow row in Rows)
        {
            row.Written = row.NoteKey.Length == 0 || written.Contains(row.NoteKey);
        }
    }

    private void Unbind(HostRow row)
    {
        string key = row.NoteKey;
        row.Unbind();

        if (!row.Names().Contains(key, StringComparer.OrdinalIgnoreCase)
            && _index.TryGetValue(key, out HostRow? known) && ReferenceEquals(known, row))
        {
            _index.Remove(key);
        }
    }

    /// <summary>Mette in tabella, in attesa, gli indirizzi nuovi; torna quelli appena arrivati.</summary>
    public IReadOnlyList<string> EnsurePending(IEnumerable<string> hosts)
    {
        List<string> fresh = [];

        foreach (string host in hosts)
        {
            if (host.Length == 0 || _hidden.Contains(host))
            {
                continue;
            }

            if (Find(host) is { } known)
            {
                // una riga trovata per intervallo che ora e' anche scritta nella nota la segue
                if (known.NoteKey.Length == 0 && known.IP.Equals(host, StringComparison.OrdinalIgnoreCase))
                {
                    known.Bind(host);
                    _index[host] = known;
                }

                continue;
            }

            HostRow row = HostRow.Pending(host);
            _index[host] = row;
            Insert(row);
            fresh.Add(host);
        }

        return fresh;
    }

    public IReadOnlyList<string> NeverChecked(IEnumerable<string> among)
    {
        HashSet<string> asked = new(among, StringComparer.OrdinalIgnoreCase);

        return [.. Rows.Where(row => !row.IsChecked)
            .Select(row => row.NoteKey.Length > 0 ? row.NoteKey : row.IP)
            .Where(asked.Contains)];
    }

    public void Clear()
    {
        _index.Clear();
        Rows.Clear();
        _hidden.Clear();
        Say("Pronto.");
    }

    public IEnumerable<IReadOnlyDictionary<string, string>> Records() =>
        Rows.Where(row => row.IsChecked).Select(row => row.ToRecord());

    public void Restore(IEnumerable<IReadOnlyDictionary<string, string>> records)
    {
        foreach (IReadOnlyDictionary<string, string> record in records)
        {
            HostRow row = HostRow.FromRecord(record);

            if (row.IP.Length == 0 || row.Names().Any(_hidden.Contains) || Find(row.IP) is not null)
            {
                continue;
            }

            foreach (string name in row.Names())
            {
                _index[name] = row;
            }

            Insert(row);
        }
    }

    /// <param name="keep">false: un'analisi per intervallo toglie le righe che non c'entrano.</param>
    /// <param name="force">true: analisi completa anche di chi e' gia' stato analizzato (il
    /// comando "Analizza adesso" su un host).</param>
    /// <param name="alsoKeep">Con keep false, le righe che restano comunque: quelle dei gruppi.</param>
    public async Task RunAsync(string targets, ScanOptions options, bool keep, bool force = false, Func<HostRow, bool>? alsoKeep = null)
    {
        if (IsRunning)
        {
            return;
        }

        List<string> queue;

        try
        {
            queue = [.. TargetList.Expand(targets).Where(host => !_hidden.Contains(host))];
        }
        catch (ArgumentException problem)
        {
            Say(problem.Message);
            return;
        }

        if (queue.Count == 0)
        {
            Say(_hidden.Count > 0
                ? "Niente da analizzare: gli host indicati sono tutti fra quelli messi da parte."
                : "Indica un intervallo, per esempio 192.168.1.0/24");
            return;
        }

        if (!keep)
        {
            // fuori solo chi non e' nel nuovo intervallo: chi c'e' tiene quel che si sapeva di lui
            HashSet<string> wanted = new(queue, StringComparer.OrdinalIgnoreCase);

            for (int i = Rows.Count - 1; i >= 0; i--)
            {
                if (Rows[i].NoteKey.Length == 0 && !Rows[i].Names().Any(wanted.Contains) && alsoKeep?.Invoke(Rows[i]) != true)
                {
                    Drop(i);
                }
            }
        }

        List<string> light = force ? [] : [.. queue.Where(Known)];
        List<string> full = [.. queue.Except(light, StringComparer.OrdinalIgnoreCase)];

        _done = 0;
        _total = queue.Count;
        IsRunning = true;
        _stop = new CancellationTokenSource();
        Stopwatch clock = Stopwatch.StartNew();
        RaiseAll();

        try
        {
            // prima il ping di chi si conosce gia', poi l'analisi completa dei nuovi e di chi e'
            // appena tornato acceso
            _doubts.Clear();
            List<string> wokeUp = await Pass(light, Ping(options), detailed: false);
            full.AddRange(wokeUp.Where(host => !full.Contains(host, StringComparer.OrdinalIgnoreCase)));
            _total = light.Count + full.Count;
            await Pass(full, options, detailed: true);
            await Confirm(options);

            // attivi fra quelli di questo giro: con keep la tabella ne ha anche altri
            int up = queue.Count(host => Find(host) is { IsUp: true });
            Say(string.Create(CultureInfo.CurrentCulture, $"{up} host attivi su {queue.Count}, in {clock.Elapsed.TotalSeconds:F1} s."));
        }
        catch (OperationCanceledException)
        {
            // fermata a meta': chi era in dubbio resta com'era (acceso), nessuno e' spento per caso
            _doubts.Clear();
            Say(string.Create(CultureInfo.CurrentCulture, $"Fermata. {UpCount} host trovati in {clock.Elapsed.TotalSeconds:F1} s."));
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            Say($"Analisi interrotta da un errore: {problem.Message}. Riprova, o restringi l'intervallo.");
        }
        finally
        {
            _stop.Dispose();
            _stop = null;
            IsRunning = false;
            RaiseAll();
            Finished?.Invoke(Message);
        }
    }

    /// <summary>Gia' analizzato per intero in questo avvio, e ancora in tabella?</summary>
    private bool Known(string host) => _full.Contains(host) && Find(host) is { IsChecked: true };

    /// <summary>Un giro dello scanner. Torna gli host che erano spenti e ora rispondono.</summary>
    private async Task<List<string>> Pass(List<string> hosts, ScanOptions options, bool detailed)
    {
        List<string> wokeUp = [];

        if (hosts.Count == 0 || _stop is null)
        {
            return wokeUp;
        }

        await foreach (HostScanResult result in new NetworkScanner(options, _vendors).ScanAsync(hosts, _stop.Token))
        {
            // la tabella e' dell'interfaccia: i risultati si consegnano sempre sul suo thread
            if (UiThread.Elsewhere())
            {
                await UiThread.Run(() => Take(result, detailed, wokeUp));
            }
            else
            {
                Take(result, detailed, wokeUp);
            }
        }

        return wokeUp;
    }

    private void Take(HostScanResult result, bool detailed, List<string> wokeUp)
    {
        _done++;
        string target = result.Input.Length > 0 ? result.Input : result.Address;
        HostRow? known = (result.Input.Length > 0 ? Find(result.Input) : null) ?? Find(result.Address);

        if (known is { IsUp: true } && !result.IsAlive)
        {
            // era acceso e stavolta non ha risposto: un ping perso sotto il carico dell'analisi
            // non basta per dirlo spento. Resta com'era, coi suoi dettagli, fino alla verifica.
            _doubts.Add(new Doubt(target, known, result));
        }
        else if (detailed)
        {
            Absorb(result);
            _full.Add(target);
            _full.Add(result.Address);
        }
        else if (known is not null)
        {
            bool wasUp = known.IsUp;
            known.Refresh(result);
            RaiseAll();

            if (!wasUp && known.IsUp)
            {
                wokeUp.Add(target);
            }
        }
        else
        {
            Absorb(result);
        }

        // il messaggio conta le righe: a ogni risultato, su una rete grande, sarebbe un lavoro
        // quadratico. Lo si aggiorna qualche volta al secondo, che e' quanto si riesce a leggere.
        if (_progress.ElapsedMilliseconds >= 120 || _done == _total)
        {
            _progress.Restart();
            Say(string.Create(CultureInfo.CurrentCulture, $"{_done} di {_total} analizzati, {UpCount} attivi..."));
        }
    }

    private readonly Stopwatch _progress = Stopwatch.StartNew();

    /// <summary>
    /// La verifica degli host accesi che non hanno risposto, a giro finito e con la rete calma:
    /// pochi alla volta, ping con piu' pazienza e le porte che avevano aperte. Spento solo chi
    /// non risponde neanche cosi'; chi risponde resta acceso coi suoi dettagli.
    /// </summary>
    private async Task Confirm(ScanOptions options)
    {
        if (_doubts.Count == 0 || _stop is null)
        {
            return;
        }

        List<Doubt> doubts = [.. _doubts];
        _doubts.Clear();
        Say(string.Create(CultureInfo.CurrentCulture, $"Ricontrollo {doubts.Count} host che non hanno risposto..."));

        TimeSpan patience = TimeSpan.FromMilliseconds(Math.Clamp(options.PingTimeout.TotalMilliseconds * 1.5, 1000, 4000));
        CancellationToken token = _stop.Token;
        using SemaphoreSlim calm = new(16);

        HostScanResult[] seconds = await Task.WhenAll(doubts.Select(async doubt =>
        {
            await calm.WaitAsync(token).ConfigureAwait(false);

            try
            {
                return await HostProbe.ConfirmAsync(doubt.Target, doubt.First.Address, KnownPorts(doubt.Row), patience, token).ConfigureAwait(false);
            }
            finally
            {
                calm.Release();
            }
        }));

        void Settle()
        {
            for (int i = 0; i < doubts.Count; i++)
            {
                HostRow row = doubts[i].Row;
                HostScanResult second = seconds[i];

                // la riga puo' essere uscita intanto (messa da parte, nota cambiata)
                if (!Rows.Contains(row))
                {
                    continue;
                }

                // ha risposto al ping: tempi nuovi; solo da una porta: resta com'era
                if (second.Status == HostStatus.Online || !second.IsAlive)
                {
                    row.Refresh(second);
                }
            }

            RaiseAll();
        }

        if (UiThread.Elsewhere())
        {
            await UiThread.Run(Settle);
        }
        else
        {
            Settle();
        }
    }

    /// <summary>Le porte che l'host aveva aperte, per la verifica (al massimo tre).</summary>
    private static IReadOnlyList<int> KnownPorts(HostRow row) =>
        [.. Regex.Matches(row.OpenPorts, @"\d+")
            .Select(match => int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : 0)
            .Where(port => port is > 0 and < 65536)
            .Take(3)];

    /// <summary>Le stesse regole, ma solo il ping: niente porte, nomi o sonde.</summary>
    private static ScanOptions Ping(ScanOptions full) => new()
    {
        Concurrency = full.Concurrency,
        Sockets = full.Sockets,
        PingCount = full.PingCount,
        PingTimeout = full.PingTimeout,
        PortTimeout = full.PortTimeout,
        Ports = [],
        ScanDeadHosts = false,
        ResolveDns = false,
        ProbeNetBios = false,
        ProbeMdns = false,
        ProbeSsdp = false,
        ProbeSnmp = false,
        ProbeBanners = false,
        ProbeShares = false,
        ProbeWmi = false
    };

    public void Stop()
    {
        if (IsRunning)
        {
            Say("Interruzione in corso...");
        }

        _stop?.Cancel();
    }

    private void Absorb(HostScanResult result)
    {
        HostRow? row = (result.Input.Length > 0 ? Find(result.Input) : null) ?? Find(result.Address);

        if (row is null)
        {
            row = new HostRow();
            row.Apply(result);
            Insert(row);
        }
        else
        {
            long before = row.SortKey;
            row.Apply(result);

            // la riga si sposta solo se e' cambiato il suo posto (un nome che diventa un IP)
            if (before != row.SortKey)
            {
                int from = Rows.IndexOf(row);
                Rows.Move(from, Place(row, from));
            }
        }

        foreach (string name in row.Names())
        {
            _index[name] = row;
        }

        RaiseAll();
    }

    private void Insert(HostRow row) => Rows.Insert(Place(row, -1), row);

    /// <summary>
    /// Dove va la riga per restare in ordine di indirizzo. Una riga nuova (self = -1) si cerca
    /// con la ricerca binaria: la tabella e' sempre ordinata e su una rete grande le righe sono
    /// migliaia. Una riga che si sposta e' rara: li' si contano le altre.
    /// </summary>
    private int Place(HostRow row, int self)
    {
        if (self >= 0)
        {
            int at = 0;

            for (int i = 0; i < Rows.Count; i++)
            {
                if (i != self && Rows[i].SortKey <= row.SortKey)
                {
                    at++;
                }
            }

            return at;
        }

        int low = 0;
        int high = Rows.Count;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (Rows[middle].SortKey <= row.SortKey)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void Drop(int index)
    {
        foreach (string name in Rows[index].Names())
        {
            _index.Remove(name);
        }

        Rows.RemoveAt(index);
        RaiseAll();
    }

    private void Say(string message)
    {
        Message = message;
        RaiseAll();
    }
}
