namespace DuckNote.App.Storage;

/// <summary>
/// Le scritture su disco (nota, cassaforte, ultima scansione) fuori dal thread dell'interfaccia,
/// una alla volta e nell'ordine in cui sono state chieste: comprimere, cifrare e scrivere non
/// fermano mai chi sta scrivendo. Due richieste con la stessa chiave ancora in attesa diventano
/// una sola, l'ultima: salvare tre volte di fila la nota vuol dire scrivere l'ultima versione.
/// </summary>
public sealed class IoQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Action> _waiting = [];
    private Task _tail = Task.CompletedTask;

    /// <summary>Un lavoro e' fallito (chiave, errore). Arriva dal thread di lavoro.</summary>
    public event Action<string, Exception>? Failed;

    public void Post(string key, Action job)
    {
        lock (_gate)
        {
            bool queued = _waiting.ContainsKey(key);
            _waiting[key] = job;

            if (!queued)
            {
                _tail = _tail.ContinueWith(_ => Run(key), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// Aspetta che tutto quello che e' in coda sia scritto. Serve prima di chiudere, di bloccare
    /// la nota o di toccare la cassaforte a mano: da li' in poi nessun lavoro e' in corso.
    /// </summary>
    public void Drain()
    {
        Task tail;

        lock (_gate)
        {
            tail = _tail;
        }

        tail.Wait();
    }

    private void Run(string key)
    {
        Action? job;

        lock (_gate)
        {
            _waiting.Remove(key, out job);
        }

        try
        {
            job?.Invoke();
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            Failed?.Invoke(key, problem);
        }
    }
}
