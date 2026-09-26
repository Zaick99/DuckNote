using System.Net;
using DuckNote.Scan.Wire;

namespace DuckNote.Scan.Probes;

public static class NameProbe
{
    private const int DnsPort = 53;
    private const int NetBiosPort = 137;
    private const int MdnsPort = 5353;

    public static async Task<string> ReverseAsync(
        string address, string dnsServer, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (dnsServer.Length > 0)
        {
            return await QueryAsync(dnsServer, DnsMessage.ArpaName(address), DnsMessage.TypePtr, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        return await Abandon(
            async () =>
            {
                IPHostEntry entry = await Dns.GetHostEntryAsync(address, cancellationToken).ConfigureAwait(false);
                return entry.HostName == address ? string.Empty : entry.HostName;
            },
            timeout, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ResolveAsync(
        string name, string dnsServer, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (dnsServer.Length > 0)
        {
            return await QueryAsync(dnsServer, name, DnsMessage.TypeA, timeout, cancellationToken).ConfigureAwait(false);
        }

        return await Abandon(
            async () =>
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(name, cancellationToken).ConfigureAwait(false);
                IPAddress? v4 = Array.Find(addresses,
                    a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return v4?.ToString() ?? string.Empty;
            },
            timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> Abandon(
        Func<Task<string>> lookup, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<string> resolution;
        try
        {
            resolution = lookup();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return string.Empty;
        }

        Task finished = await Task.WhenAny(resolution, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, resolution))
        {
            Forget(resolution);
            return string.Empty;
        }

        try
        {
            return await resolution.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException
                                      or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static void Forget(Task task) =>
        _ = task.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public static async Task<string> QueryAsync(
        string server, string name, int type, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (name.Length == 0)
        {
            return string.Empty;
        }

        byte[] query = DnsMessage.BuildQuery(name, type, UdpExchange.NewTransactionId());
        byte[]? reply = await UdpExchange.AskAsync(server, DnsPort, query, timeout, cancellationToken).ConfigureAwait(false);

        return reply is null ? string.Empty : DnsMessage.ReadAnswer(reply, type);
    }

    public static async Task<NetBiosMessage.NodeStatus> NetBiosAsync(
        string address, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        byte[] query = NetBiosMessage.BuildNodeStatusQuery(UdpExchange.NewTransactionId());
        byte[]? reply = await UdpExchange.AskAsync(address, NetBiosPort, query, timeout, cancellationToken)
                                         .ConfigureAwait(false);

        return reply is null ? NetBiosMessage.NodeStatus.Empty : NetBiosMessage.ParseNodeStatus(reply);
    }

    public static async Task<string> MdnsAsync(
        string address, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        string arpa = DnsMessage.ArpaName(address);
        if (arpa.Length == 0)
        {
            return string.Empty;
        }

        List<byte> packet = [0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        packet.AddRange(DnsMessage.EncodeLabels(arpa));
        packet.AddRange([0x00, 0x0C, 0x80, 0x01]);

        byte[]? reply = await UdpExchange.AskAsync(address, MdnsPort, [.. packet], timeout, cancellationToken)
                                         .ConfigureAwait(false);

        return reply is null ? string.Empty : DnsMessage.ReadAnswer(reply, DnsMessage.TypePtr);
    }
}
