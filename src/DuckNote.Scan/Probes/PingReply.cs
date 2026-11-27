namespace DuckNote.Scan.Probes;

public sealed record PingOutcome(bool Online, IReadOnlyList<long> RoundTrips, int Ttl, int Sent, int Received)
{
    public static readonly PingOutcome Silent = new(false, [], 0, 0, 0);

    /// <summary>
    /// Nessun tentativo e' arrivato all'host: e' fallito questo computer (troppi ping insieme,
    /// risorse finite). Non vuol dire che l'host sia spento.
    /// </summary>
    public bool Inconclusive { get; init; }

    public double AverageMs => RoundTrips.Count == 0 ? 0 : Math.Round(RoundTrips.Average(), 1);

    public int LossPercent => Sent == 0 ? 0 : (int)((Sent - Received) / (double)Sent * 100);

    public string Describe()
    {
        if (RoundTrips.Count == 0)
        {
            return string.Empty;
        }
        if (RoundTrips.Count == 1)
        {
            return $"{AverageMs} ms";
        }
        return $"{AverageMs} ms (min {RoundTrips.Min()} / max {RoundTrips.Max()})";
    }
}
