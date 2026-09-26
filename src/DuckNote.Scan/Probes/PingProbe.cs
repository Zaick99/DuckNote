using System.Net.NetworkInformation;

namespace DuckNote.Scan.Probes;

public static class PingProbe
{
    private static readonly byte[] Payload = new byte[32];

    public static async Task<PingOutcome> SendAsync(
        string target, int count, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        List<long> roundTrips = [];
        int received = 0;
        int ttl = 0;

        using Ping ping = new();
        PingOptions options = new() { DontFragment = true };

        for (int attempt = 0; attempt < count; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PingReply reply;
            try
            {
                reply = await ping.SendPingAsync(target, (int)timeout.TotalMilliseconds, Payload, options)
                                  .ConfigureAwait(false);
            }
            catch (PingException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (reply.Status != IPStatus.Success)
            {
                continue;
            }

            received++;
            roundTrips.Add(reply.RoundtripTime);
            if (reply.Options is { Ttl: > 0 })
            {
                ttl = reply.Options.Ttl;
            }
        }

        return new PingOutcome(received > 0, roundTrips, ttl, count, received);
    }
}
