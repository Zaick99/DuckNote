using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DuckNote.Scan.Probes;

public static partial class SharesProbe
{
    public static async Task<IReadOnlyList<string>> ListAsync(
        string address, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Command(address) is not { } startup)
        {
            return [];
        }

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

    /// <summary>
    /// Il comando del sistema che elenca le condivisioni: "net view \\host /all" su Windows,
    /// "smbutil view -N //host" su macOS (senza chiedere la password), "smbclient -N -L //host"
    /// su Linux se c'e'. Null dove non c'e' niente da usare.
    /// </summary>
    private static ProcessStartInfo? Command(string address)
    {
        ProcessStartInfo startup = new()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startup.FileName = Path.Combine(Environment.SystemDirectory, "net.exe");
            startup.ArgumentList.Add("view");
            startup.ArgumentList.Add($@"\\{address}");
            startup.ArgumentList.Add("/all");
            return startup;
        }

        if (OperatingSystem.IsMacOS())
        {
            startup.FileName = "/usr/bin/smbutil";
            startup.ArgumentList.Add("view");
            startup.ArgumentList.Add("-N");
            startup.ArgumentList.Add($"//{address}");
            return startup;
        }

        string? client = ((string[])["/usr/bin/smbclient", "/usr/local/bin/smbclient"]).FirstOrDefault(File.Exists);

        if (client is null)
        {
            return null;
        }

        startup.FileName = client;
        startup.ArgumentList.Add("-N");
        startup.ArgumentList.Add("-g");
        startup.ArgumentList.Add("-L");
        startup.ArgumentList.Add($"//{address}");
        return startup;
    }

    public static IReadOnlyList<string> ReadShares(string output)
    {
        List<string> shares = [];
        foreach (string line in output.Split('\n'))
        {
            // smbclient -g scrive "Disk|nome|commento"
            string[] grep = line.TrimEnd('\r').Split('|');
            if (grep.Length >= 2 && grep[0] is "Disk" or "Printer")
            {
                shares.Add(grep[1]);
                continue;
            }

            Match match = ShareRow().Match(line.TrimEnd('\r').TrimStart());
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

    [GeneratedRegex(@"^(\S+)\s+(Disk|Disco|Print|Printer|Stampa|IPC)\b")]
    private static partial Regex ShareRow();
}
