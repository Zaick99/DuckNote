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
                FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "arp.exe")
                    : File.Exists("/usr/sbin/arp") ? "/usr/sbin/arp" : "arp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            // -n: niente nomi, sarebbe una ricerca DNS per riga
            startup.ArgumentList.Add(OperatingSystem.IsWindows() ? "-a" : "-an");

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

                foreach ((string address, string mac) in ParseTable(await output.ConfigureAwait(false)))
                {
                    entries[address] = mac;
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

    /// <summary>
    /// Le righe della tabella ARP, comunque le scriva il sistema: "192.168.1.1  00-11-22-aa-bb-cc"
    /// (Windows), "? (192.168.1.1) at 0:11:22:a:bb:cc on en0" (macOS, che toglie gli zeri in
    /// testa) o "? (192.168.1.1) at 00:11:22:aa:bb:cc [ether] on eth0" (Linux). Il MAC esce
    /// sempre come 00:11:22:AA:BB:CC; le righe incomplete si saltano.
    /// </summary>
    public static IEnumerable<(string Address, string Mac)> ParseTable(string output)
    {
        foreach (Match row in NeighbourRow().Matches(output))
        {
            yield return (row.Groups[1].Value, row.Groups[2].Value.ToUpperInvariant().Replace('-', ':'));
        }

        foreach (Match row in UnixRow().Matches(output))
        {
            string[] octets = row.Groups[2].Value.Split(':');
            yield return (row.Groups[1].Value, string.Join(':', octets.Select(octet => octet.PadLeft(2, '0').ToUpperInvariant())));
        }
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5})")]
    private static partial Regex NeighbourRow();

    [GeneratedRegex(@"\((\d{1,3}(?:\.\d{1,3}){3})\)\s+at\s+([0-9A-Fa-f]{1,2}(?::[0-9A-Fa-f]{1,2}){5})\b")]
    private static partial Regex UnixRow();
}
