using System.Globalization;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace DuckNote.Scan.Probes;

public static class WmiProbe
{
    private const string Namespace = @"root\cimv2";
    private const string Dialect = "WQL";

    public static Task<WindowsInventory> ReadAsync(
        string address, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(address, timeout), cancellationToken);

    private static WindowsInventory Read(string address, TimeSpan timeout)
    {
        try
        {
            WSManSessionOptions options = new() { Timeout = timeout };
            using CimSession session = CimSession.Create(address, options);

            string operatingSystem = ReadOperatingSystem(session, out string uptime, out string ram);
            string model = ReadComputerSystem(session, out string user, out string domain);

            return new WindowsInventory
            {
                OperatingSystem = operatingSystem,
                Uptime = uptime,
                Ram = ram,
                Model = model,
                User = user,
                Domain = domain,
                Serial = ReadFirst(session, "SELECT SerialNumber FROM Win32_BIOS", "SerialNumber", 60),
                Cpu = ReadProcessor(session),
                Disks = ReadDisks(session)
            };
        }
        catch (CimException)
        {
            return WindowsInventory.Empty;
        }
        catch (InvalidOperationException)
        {
            return WindowsInventory.Empty;
        }
    }

    private static string ReadOperatingSystem(CimSession session, out string uptime, out string ram)
    {
        uptime = string.Empty;
        ram = string.Empty;

        CimInstance? instance = FirstOrDefault(session, "SELECT * FROM Win32_OperatingSystem");
        if (instance is null)
        {
            return string.Empty;
        }

        using (instance)
        {
            if (Value(instance, "LastBootUpTime") is DateTime booted)
            {
                TimeSpan running = DateTime.Now - booted;
                uptime = $"{running.Days}g {running.Hours:00}:{running.Minutes:00}";
            }

            if (Value(instance, "TotalVisibleMemorySize") is ulong kilobytes)
            {
                ram = string.Format(CultureInfo.InvariantCulture, "{0:N1} GB", kilobytes / 1048576.0);
            }

            return TextSanitiser.Clean(
                $"{Text(instance, "Caption")} {Text(instance, "OSArchitecture")} build {Text(instance, "BuildNumber")}",
                120);
        }
    }

    private static string ReadComputerSystem(CimSession session, out string user, out string domain)
    {
        user = string.Empty;
        domain = string.Empty;

        CimInstance? instance = FirstOrDefault(session, "SELECT * FROM Win32_ComputerSystem");
        if (instance is null)
        {
            return string.Empty;
        }

        using (instance)
        {
            user = TextSanitiser.Clean(Text(instance, "UserName"), 80);
            domain = TextSanitiser.Clean(Text(instance, "Domain"), 80);
            return TextSanitiser.Clean($"{Text(instance, "Manufacturer")} {Text(instance, "Model")}", 100);
        }
    }

    private static string ReadProcessor(CimSession session)
    {
        CimInstance? instance = FirstOrDefault(session, "SELECT * FROM Win32_Processor");
        if (instance is null)
        {
            return string.Empty;
        }

        using (instance)
        {
            return TextSanitiser.Clean(
                $"{Text(instance, "Name")} ({Text(instance, "NumberOfCores")}C/{Text(instance, "NumberOfLogicalProcessors")}T)",
                110);
        }
    }

    private static string ReadDisks(CimSession session)
    {
        List<string> disks = [];
        foreach (CimInstance disk in session.QueryInstances(
            Namespace, Dialect, "SELECT * FROM Win32_LogicalDisk WHERE DriveType=3"))
        {
            using (disk)
            {
                ulong free = Value(disk, "FreeSpace") as ulong? ?? 0;
                ulong size = Value(disk, "Size") as ulong? ?? 0;
                disks.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:N0}/{2:N0} GB",
                    Text(disk, "DeviceID"), free / 1073741824.0, size / 1073741824.0));
            }
        }

        return string.Join(" | ", disks);
    }

    private static string ReadFirst(CimSession session, string query, string property, int limit)
    {
        CimInstance? instance = FirstOrDefault(session, query);
        if (instance is null)
        {
            return string.Empty;
        }

        using (instance)
        {
            return TextSanitiser.Clean(Text(instance, property), limit);
        }
    }

    private static CimInstance? FirstOrDefault(CimSession session, string query)
    {
        foreach (CimInstance instance in session.QueryInstances(Namespace, Dialect, query))
        {
            return instance;
        }
        return null;
    }

    private static object? Value(CimInstance instance, string property)
    {
        try
        {
            return instance.CimInstanceProperties[property]?.Value;
        }
        catch (CimException)
        {
            return null;
        }
    }

    private static string Text(CimInstance instance, string property) =>
        Value(instance, property)?.ToString() ?? string.Empty;
}
