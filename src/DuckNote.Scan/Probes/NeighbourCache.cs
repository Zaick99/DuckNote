using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DuckNote.Scan.Probes;

public static partial class NeighbourCache
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(2);
    private static readonly Lock Gate = new();

    private static Dictionary<string, string> _entries = [];
    private static DateTime _read = DateTime.MinValue;

    public static string Lookup(string address)
    {
        Dictionary<string, string> entries = Current();
        return entries.TryGetValue(address, out string? mac) ? mac : string.Empty;
    }

    private static Dictionary<string, string> Current()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _read < Freshness)
            {
                return _entries;
            }

            _entries = Read();
            _read = DateTime.UtcNow;
            return _entries;
        }
    }

    private static Dictionary<string, string> Read()
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
                return entries;
            }

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000))
            {
                return entries;
            }

            foreach (Match row in NeighbourRow().Matches(output))
            {
                entries[row.Groups[1].Value] = row.Groups[2].Value.ToUpperInvariant().Replace('-', ':');
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return entries;
        }

        return entries;
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5})")]
    private static partial Regex NeighbourRow();
}
