using System.Net.Sockets;

namespace DuckNote.Scan.Probes;

public static class PortProbe
{
    public static async Task<IReadOnlyList<int>> ScanAsync(
        string address, IReadOnlyList<int> ports, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (ports.Count == 0)
        {
            return [];
        }

        IEnumerable<Task<int?>> attempts = ports.Select(port => TryConnectAsync(address, port, timeout, cancellationToken));
        int?[] outcomes = await Task.WhenAll(attempts).ConfigureAwait(false);

        return [.. outcomes.Where(port => port.HasValue).Select(port => port!.Value).Order()];
    }

    private static async Task<int?> TryConnectAsync(
        string address, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(address, port, deadline.Token).ConfigureAwait(false);
            return client.Connected ? port : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
