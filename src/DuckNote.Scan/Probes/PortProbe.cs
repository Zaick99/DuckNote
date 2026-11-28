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

    /// <summary>
    /// L'host e' acceso? Si prova a connettersi alle porte date, insieme: basta una porta che
    /// accetta o che rifiuta la connessione (un rifiuto e' l'host che risponde "qui no"). Il
    /// silenzio su tutte (scadenza, host o rete irraggiungibile) non prova niente.
    /// </summary>
    public static async Task<bool> AnswersAsync(
        string address,
        IReadOnlyList<int> ports,
        TimeSpan timeout,
        SocketBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        if (ports.Count == 0)
        {
            return false;
        }

        using CancellationTokenSource done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<Task<bool>> pending = [.. ports.Distinct().Select(port => KnocksAsync(address, port, timeout, budget, done.Token))];
        try
        {
            while (pending.Count > 0)
            {
                Task<bool> finished = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(finished);
                if (await finished.ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            done.Cancel();
        }
    }

    private static async Task<bool> KnocksAsync(
        string address, int port, TimeSpan timeout, SocketBudget? budget, CancellationToken cancellationToken)
    {
        try
        {
            using IDisposable? slot = budget is null
                ? null
                : await budget.TakeAsync(cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            using TcpClient client = new();
            await client.ConnectAsync(address, port, deadline.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (SocketException refused) when (refused.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
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
