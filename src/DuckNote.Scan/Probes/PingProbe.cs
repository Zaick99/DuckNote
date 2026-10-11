using System.Net.NetworkInformation;

namespace DuckNote.Scan.Probes;

public static class PingProbe
{
    private static readonly byte[] Payload = new byte[32];

    public static async Task<PingOutcome> SendAsync(
        string target, int count, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int attempts = Math.Max(1, count);

        PingReply?[] replies = await Task.WhenAll(
            Enumerable.Range(0, attempts).Select(_ => OnceAsync(target, timeout, cancellationToken)))
            .ConfigureAwait(false);

        List<long> roundTrips = [];
        int ttl = 0;

        foreach (PingReply? reply in replies)
        {
            if (reply is not { Status: IPStatus.Success })
            {
                continue;
            }

            roundTrips.Add(reply.RoundtripTime);

            if (reply.Options is { Ttl: > 0 })
            {
                ttl = reply.Options.Ttl;
            }
        }

        return new PingOutcome(roundTrips.Count > 0, roundTrips, ttl, attempts, roundTrips.Count);
    }

    private static async Task<PingReply?> OnceAsync(
        string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using Ping ping = new();
        PingOptions options = new() { DontFragment = true };

        try
        {
            return await ping.SendPingAsync(target, (int)timeout.TotalMilliseconds, Payload, options)
                             .WaitAsync(cancellationToken)
                             .ConfigureAwait(false);
        }
        catch (Exception problem) when (problem is PingException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
