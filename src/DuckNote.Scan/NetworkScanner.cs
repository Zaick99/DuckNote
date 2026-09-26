using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DuckNote.Scan;

public sealed class NetworkScanner(ScanOptions options, IVendorLookup vendors)
{
    public async IAsyncEnumerable<HostScanResult> ScanAsync(
        IEnumerable<string> targets,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string[] queue = [.. targets
            .Select(target => target.Trim())
            .Where(target => target.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (queue.Length == 0)
        {
            yield break;
        }

        Channel<HostScanResult> results = Channel.CreateUnbounded<HostScanResult>(
            new UnboundedChannelOptions { SingleReader = true });

        Task producer = ProduceAsync(queue, results.Writer, cancellationToken);

        await foreach (HostScanResult result in results.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }

        await producer.ConfigureAwait(false);
    }

    private async Task ProduceAsync(
        IReadOnlyList<string> targets, ChannelWriter<HostScanResult> writer, CancellationToken cancellationToken)
    {
        using SemaphoreSlim gate = new(Math.Max(1, options.Concurrency));
        HostProbe probe = new(options, vendors);
        Exception? failure = null;

        try
        {
            IEnumerable<Task> work = targets.Select(async target =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    HostScanResult result = await probe.InspectAsync(target, cancellationToken).ConfigureAwait(false);
                    await writer.WriteAsync(result, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(work).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }
}
