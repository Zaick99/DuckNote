using System.Net;
using System.Runtime.InteropServices;

namespace DuckNote.Scan.Probes;

public static class ArpProbe
{
    private static readonly SemaphoreSlim Resolvers = new(64);

    public static string FromCache(string address)
    {
        if (!IsReachableByArp(address))
        {
            return string.Empty;
        }

        string own = LocalNetwork.OwnMac(address);
        return own.Length > 0 ? own : NeighbourCache.Lookup(address);
    }

    public static async Task<string> ResolveAsync(string address, CancellationToken cancellationToken = default)
    {
        string cached = FromCache(address);
        if (cached.Length > 0)
        {
            return cached;
        }
        if (!IsReachableByArp(address))
        {
            return string.Empty;
        }

        await Resolvers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Factory.StartNew(
                () => SendRequest(address),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).ConfigureAwait(false);
        }
        finally
        {
            Resolvers.Release();
        }
    }

    public static string SendRequest(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress? parsed))
        {
            return string.Empty;
        }

#pragma warning disable CS0618 // Address serve proprio come uint per SendARP
        uint destination = (uint)parsed.Address;
#pragma warning restore CS0618
        if (destination == 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[6];
        uint length = 6;

        try
        {
            if (SendARP(destination, 0, buffer, ref length) != 0 || length < 6)
            {
                return string.Empty;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return string.Empty;
        }

        return Format(buffer, (int)length);
    }

    private static bool IsReachableByArp(string address) =>
        IPAddress.TryParse(address, out IPAddress? parsed)
        && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && LocalNetwork.IsOnLink(parsed);

    public static string Format(byte[] raw, int length) =>
        string.Join(':', raw.Take(Math.Min(length, 6)).Select(b => b.ToString("X2")));

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destination, uint source, byte[] macAddress, ref uint macLength);
}
