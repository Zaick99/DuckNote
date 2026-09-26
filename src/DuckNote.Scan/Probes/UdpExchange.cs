using System.Net;
using System.Net.Sockets;

namespace DuckNote.Scan.Probes;

public static class UdpExchange
{
    public static async Task<byte[]?> AskAsync(
        string address,
        int port,
        byte[] payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(address, out IPAddress? parsed))
        {
            return null;
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        using UdpClient client = new();
        try
        {
            await client.SendAsync(payload, new IPEndPoint(parsed, port), deadline.Token).ConfigureAwait(false);
            UdpReceiveResult reply = await client.ReceiveAsync(deadline.Token).ConfigureAwait(false);
            return reply.Buffer;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public static ushort NewTransactionId() => (ushort)Random.Shared.Next(0, ushort.MaxValue + 1);
}
