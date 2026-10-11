using System.Net.Sockets;

namespace DuckNote.Scan.Probes;

public static class PortProbe
{
    public static async Task<IReadOnlyList<int>> ScanAsync(
        string address,
        IReadOnlyList<int> ports,
        TimeSpan timeout,
        SocketBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        if (ports.Count == 0)
        {
            return [];
        }

        IEnumerable<Task<int?>> attempts =
            ports.Select(port => TryConnectAsync(address, port, timeout, budget, cancellationToken));

        int?[] outcomes = await Task.WhenAll(attempts).ConfigureAwait(false);

        return [.. outcomes.Where(port => port.HasValue).Select(port => port!.Value).Order()];
    }

    private static async Task<int?> TryConnectAsync(
        string address, int port, TimeSpan timeout, SocketBudget? budget, CancellationToken cancellationToken)
    {
        using IDisposable? slot = budget is null
            ? null
            : await budget.TakeAsync(cancellationToken).ConfigureAwait(false);

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
