using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
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

    public int WaitingCount => Rows.Count(row => !row.IsChecked);

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

    public HostRow? Find(string host) =>
        _index.TryGetValue(host, out HostRow? row)
            ? row
            : Rows.FirstOrDefault(r => r.Names().Contains(host, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Allinea la tabella alla nota: una riga nata da un indirizzo scritto lo segue, cancellato
    /// l'indirizzo se ne va. Una riga nata da un intervallo resta, se ha un esito.
    /// </summary>
    /// <summary>Restano solo gli host scritti nella nota: quelli trovati per intervallo escono.</summary>
    public int KeepOnlyNote()
    {
        int dropped = 0;

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].NoteKey.Length == 0)
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

    public void Prune(IEnumerable<string> written)
    {
        HashSet<string> keep = new(written, StringComparer.OrdinalIgnoreCase);

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            HostRow row = Rows[i];
            bool stays = row.NoteKey.Length > 0 ? keep.Contains(row.NoteKey) : row.IsChecked;

            if (!stays)
            {
                Drop(i);
            }
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
    public async Task RunAsync(string targets, ScanOptions options, bool keep, bool force = false)
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
                if (Rows[i].NoteKey.Length == 0 && !Rows[i].Names().Any(wanted.Contains))
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
            List<string> wokeUp = await Pass(light, Ping(options), detailed: false);
            full.AddRange(wokeUp.Where(host => !full.Contains(host, StringComparer.OrdinalIgnoreCase)));
            _total = light.Count + full.Count;
            await Pass(full, options, detailed: true);

            Say(string.Create(CultureInfo.CurrentCulture, $"{UpCount} host attivi su {queue.Count}, in {clock.Elapsed.TotalSeconds:F1} s."));
        }
        catch (OperationCanceledException)
        {
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
            if (System.Windows.Application.Current?.Dispatcher is { } ui && !ui.CheckAccess())
            {
                await ui.InvokeAsync(() => Take(result, detailed, wokeUp));
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

        if (detailed)
        {
            Absorb(result);
            _full.Add(result.Input.Length > 0 ? result.Input : result.Address);
            _full.Add(result.Address);
        }
        else if (((result.Input.Length > 0 ? Find(result.Input) : null) ?? Find(result.Address)) is { } row)
        {
            bool wasUp = row.IsUp;
            row.Refresh(result);
            RaiseAll();

            if (!wasUp && row.IsUp)
            {
                wokeUp.Add(result.Input.Length > 0 ? result.Input : result.Address);
            }
        }
        else
        {
            Absorb(result);
        }

        Say(string.Create(CultureInfo.CurrentCulture, $"{_done} di {_total} analizzati, {UpCount} attivi..."));
    }

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
            row.Apply(result);
            Rows.Move(Rows.IndexOf(row), Place(row, Rows.IndexOf(row)));
        }

        foreach (string name in row.Names())
        {
            _index[name] = row;
        }

        RaiseAll();
    }

    private void Insert(HostRow row) => Rows.Insert(Place(row, -1), row);

    /// <summary>Dove va la riga per restare in ordine di indirizzo (escludendo se stessa).</summary>
    private int Place(HostRow row, int self)
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
