using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DuckNote.App.Models;
using DuckNote.Scan;

namespace DuckNote.App.Scanning;

public sealed class ScanSession : INotifyPropertyChanged
{
    private readonly IVendorLookup _vendors = new OuiVendorLookup();
    private readonly Dictionary<string, ScanRow> _index = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _stop;
    private bool _running;
    private string _message = "Pronto.";
    private int _done;
    private int _total;

    public ObservableCollection<ScanRow> Rows { get; } = [];

    public bool IsRunning
    {
        get => _running;
        private set
        {
            if (_running == value)
            {
                return;
            }
            _running = value;
            Notify();
            Notify(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_running;

    public string Message
    {
        get => _message;
        private set
        {
            if (_message == value)
            {
                return;
            }
            _message = value;
            Notify();
        }
    }

    public double Progress => _total == 0 ? 0 : _done * 100.0 / _total;

    public int UpCount => Rows.Count(r => r.StatusRank <= 1);

    public int DownCount => Rows.Count(r => r.StatusRank is > 1 and < ScanRow.NeverChecked);

    public void EnsurePending(IEnumerable<string> hosts)
    {
        foreach (string host in hosts)
        {
            if (host.Length == 0 || _index.ContainsKey(host))
            {
                continue;
            }

            ScanRow row = new()
            {
                IP = host,
                NoteKey = host,
                Status = "mai controllato",
                StatusRank = ScanRow.NeverChecked,
                SortKey = ScanRow.SortableAddress(host)
            };

            _index[host] = row;
            Insert(row);
        }
    }

    public async Task RunAsync(string range, ScanOptions options, bool keepExisting = false)
    {
        if (IsRunning)
        {
            return;
        }

        IReadOnlyList<string> targets;
        try
        {
            targets = TargetList.Expand(range);
        }
        catch (ArgumentException ex)
        {
            Message = ex.Message;
            return;
        }

        if (targets.Count == 0)
        {
            Message = "Indica un intervallo, per esempio 192.168.1.0/24";
            return;
        }

        if (!keepExisting)
        {
            Rows.Clear();
            _index.Clear();
        }

        _done = 0;
        _total = targets.Count;
        IsRunning = true;
        Notify(nameof(Progress));

        _stop = new CancellationTokenSource();
        Stopwatch clock = Stopwatch.StartNew();

        try
        {
            NetworkScanner scanner = new(options, _vendors);
            await foreach (HostScanResult result in scanner.ScanAsync(targets, _stop.Token))
            {
                _done++;
                Absorb(result);
                Message = $"{_done} di {_total} analizzati, {UpCount} attivi...";
                Notify(nameof(Progress));
            }

            clock.Stop();
            Message = $"{UpCount} host attivi su {_total}, in {clock.Elapsed.TotalSeconds:F1} s.";
        }
        catch (OperationCanceledException)
        {
            clock.Stop();
            Message = $"Fermata. {UpCount} host trovati in {clock.Elapsed.TotalSeconds:F1} s.";
        }
        catch (Exception ex)
        {
            clock.Stop();
            Message = $"Analisi interrotta da un errore: {ex.Message}. Riprova, o restringi l'intervallo.";
        }
        finally
        {
            _stop.Dispose();
            _stop = null;
            IsRunning = false;
            Notify(nameof(UpCount));
            Notify(nameof(DownCount));
        }
    }

    public void Stop()
    {
        if (IsRunning)
        {
            Message = "Interruzione in corso...";
        }
        _stop?.Cancel();
    }

    private void Absorb(HostScanResult result)
    {
        if (_index.TryGetValue(result.Address, out ScanRow? known)
            || (result.Input.Length > 0 && _index.TryGetValue(result.Input, out known)))
        {
            known.Apply(result);
            _index[result.Address] = known;
            Reposition(known);
            return;
        }

        ScanRow row = new();
        row.Apply(result);
        _index[result.Address] = row;
        Insert(row);
    }

    private void Insert(ScanRow row)
    {
        int position = 0;
        while (position < Rows.Count && Rows[position].SortKey < row.SortKey)
        {
            position++;
        }
        Rows.Insert(position, row);
    }

    private void Reposition(ScanRow row)
    {
        int at = Rows.IndexOf(row);
        if (at < 0)
        {
            return;
        }

        Rows.RemoveAt(at);
        Insert(row);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
