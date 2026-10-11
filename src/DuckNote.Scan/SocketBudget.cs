namespace DuckNote.Scan;

public sealed class SocketBudget(int sockets) : IDisposable
{
    private readonly SemaphoreSlim _free = new(Math.Max(1, sockets));

    public async Task<IDisposable> TakeAsync(CancellationToken cancellationToken)
    {
        await _free.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new Slot(_free);
    }

    public void Dispose() => _free.Dispose();

    private sealed class Slot(SemaphoreSlim free) : IDisposable
    {
        private bool _given;

        public void Dispose()
        {
            if (_given)
            {
                return;
            }

            _given = true;
            free.Release();
        }
    }
}
