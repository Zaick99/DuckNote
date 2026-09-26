using System.Net.Sockets;
using System.Text;

namespace DuckNote.Scan.Probes;

public static class BannerProbe
{
    private const int MaximumBytes = 2048;

    public static async Task<string> ReadAsync(
        string address, int port, TimeSpan timeout, string? send = null, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(address, port, deadline.Token).ConfigureAwait(false);

            await using NetworkStream stream = client.GetStream();
            if (!string.IsNullOrEmpty(send))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(send), deadline.Token).ConfigureAwait(false);
                await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
            }

            using MemoryStream received = new();
            byte[] buffer = new byte[MaximumBytes];

            while (received.Length < MaximumBytes)
            {
                int read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                received.Write(buffer, 0, read);
                if (!stream.DataAvailable)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(received.ToArray());
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
