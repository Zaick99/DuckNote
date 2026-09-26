using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DuckNote.Scan.Probes;

public static partial class SharesProbe
{
    public static async Task<IReadOnlyList<string>> ListAsync(
        string address, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startup = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "net.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startup.ArgumentList.Add("view");
        startup.ArgumentList.Add($@"\\{address}");
        startup.ArgumentList.Add("/all");

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using Process? process = Process.Start(startup);
            if (process is null)
            {
                return [];
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Terminate(process);
                return [];
            }

            return ReadShares(await output.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                      or IOException or OperationCanceledException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> ReadShares(string output)
    {
        List<string> shares = [];
        foreach (string line in output.Split('\n'))
        {
            Match match = ShareRow().Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                shares.Add(match.Groups[1].Value);
            }
        }
        return [.. shares.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static void Terminate(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                      or NotSupportedException)
        {
        }
    }

    [GeneratedRegex(@"^(\S+)\s+(Disk|Disco|Print|Stampa|IPC)\b")]
    private static partial Regex ShareRow();
}
