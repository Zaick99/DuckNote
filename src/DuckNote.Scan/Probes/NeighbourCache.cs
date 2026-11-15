using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DuckNote.Scan.Probes;

/// <summary>
/// La tabella ARP del sistema (IP -> MAC), letta con "arp -a". La lettura lancia un processo:
/// non blocca mai chi chiede. Chi chiede riceve quel che si sa adesso; se e' vecchio parte UNA
/// rilettura in un thread a parte, con un tempo massimo, e chi vuole puo' aspettarla senza
/// tenere fermo nessun thread. Con centinaia di host analizzati insieme, un "arp" lento non
/// deve mettere in fila l'intera analisi dietro a un lucchetto.
/// </summary>
public static partial class NeighbourCache
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);
    private static readonly Lock Gate = new();

    private static volatile Dictionary<string, string> _entries = [];
    private static DateTime _read = DateTime.MinValue;
    private static Task _reading = Task.CompletedTask;

    /// <summary>Quel che si sa adesso, subito. Se e' vecchio, la rilettura parte in sottofondo.</summary>
    public static string Lookup(string address)
    {
        Refresh();
        return _entries.TryGetValue(address, out string? mac) ? mac : string.Empty;
    }

    /// <summary>Come <see cref="Lookup"/>, ma se l'indirizzo manca aspetta (poco) la rilettura in corso.</summary>
    public static async Task<string> LookupAsync(string address, CancellationToken cancellationToken = default)
    {
        Task reading = Refresh();

        if (!_entries.ContainsKey(address) && !reading.IsCompleted)
        {
            await Task.WhenAny(reading, Task.Delay(Patience, cancellationToken)).ConfigureAwait(false);
        }

        return _entries.TryGetValue(address, out string? mac) ? mac : string.Empty;
    }

    private static Task Refresh()
    {
        lock (Gate)
        {
            if (!_reading.IsCompleted || DateTime.UtcNow - _read < Freshness)
            {
                return _reading;
            }

            _read = DateTime.UtcNow;
            _reading = Task.Run(ReadAsync);
            return _reading;
        }
    }

    private static async Task ReadAsync()
    {
        Dictionary<string, string> entries = [];

        try
        {
            ProcessStartInfo startup = new()
            {
                FileName = Path.Combine(Environment.SystemDirectory, "arp.exe"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startup.ArgumentList.Add("-a");

            using Process? process = Process.Start(startup);
            if (process is null)
            {
                return;
            }

            using CancellationTokenSource deadline = new(Patience);

            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);

                foreach (Match row in NeighbourRow().Matches(await output.ConfigureAwait(false)))
                {
                    entries[row.Groups[1].Value] = row.Groups[2].Value.ToUpperInvariant().Replace('-', ':');
                }
            }
            catch (OperationCanceledException)
            {
                // "arp" non risponde: lo si chiude e si tiene la tabella di prima
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception problem) when (problem is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // gia' uscito da solo
                }

                return;
            }

            _entries = entries;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // senza "arp" (o senza permesso) i MAC arrivano solo dalle richieste dirette
        }
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5})")]
    private static partial Regex NeighbourRow();
}
