using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DuckNote.Scan.Probes;

/// <summary>
/// Il ping di un host.
/// <para>
/// I tentativi partono a scaglioni: il primo subito, gli altri <see cref="Stagger"/> dopo
/// l'altro, senza aspettare che il precedente scada (un host spento costa poco piu' di un
/// timeout). Il primo pacchetto verso un host che il sistema non ha ancora nella tabella ARP
/// aspetta la risoluzione, e macOS ne tiene in attesa uno solo: un secondo pacchetto mandato
/// insieme si perdeva, uno mandato poco dopo trova la strada pronta. La prima risposta basta e
/// ferma i tentativi rimasti.
/// </para>
/// <para>
/// Tutti i ping del processo passano da un cancello (<see cref="MaxInFlight"/>): centinaia di
/// socket ICMP aperti insieme perdono risposte (sul Mac ogni socket riceve le risposte di tutti e
/// i buffer si riempiono) e su Windows esauriscono le risorse del driver ICMP.
/// </para>
/// <para>
/// Un tentativo fallito per un problema di questo computer (risorse finite, permessi) non vuol
/// dire "nessuna risposta": se nessun tentativo e' arrivato all'host, l'esito e' incerto
/// (<see cref="PingOutcome.Inconclusive"/>), non spento.
/// </para>
/// </summary>
public static class PingProbe
{
    private static readonly byte[] Payload = new byte[32];

    /// <summary>Quanti ping possono essere in volo insieme, in tutto il processo.</summary>
    public const int MaxInFlight = 256;

    /// <summary>Il ritardo fra un tentativo e il successivo dello stesso host.</summary>
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(300);

    private static readonly SemaphoreSlim Gate = new(MaxInFlight);

    private sealed record Attempt(bool Sent, PingReply? Reply, bool FailedHere);

    private static readonly Attempt Skipped = new(false, null, false);

    /// <param name="stagger">Il ritardo fra un tentativo e l'altro: di serie <see cref="Stagger"/>;
    /// uguale a <paramref name="timeout"/> (o piu') per tentativi uno dopo l'altro.</param>
    public static async Task<PingOutcome> SendAsync(
        string target, int count, TimeSpan timeout, CancellationToken cancellationToken = default, TimeSpan? stagger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int attempts = Math.Max(1, count);
        TimeSpan step = stagger ?? Stagger;
        using CancellationTokenSource answered = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = answered.Token;

        List<Task<Attempt>> pending = [.. Enumerable.Range(0, attempts).Select(index => TryAsync(target, timeout, step * index, token))];

        int sent = 0;
        int local = 0;
        List<long> roundTrips = [];
        int ttl = 0;

        try
        {
            while (pending.Count > 0)
            {
                Task<Attempt> finished = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(finished);
                Attempt attempt = await finished.ConfigureAwait(false);

                if (!attempt.Sent)
                {
                    continue;
                }

                sent++;

                if (attempt.Reply is { Status: IPStatus.Success } reply)
                {
                    roundTrips.Add(reply.RoundtripTime);

                    // solo su Windows il TTL della risposta e' quello dell'host: su macOS e Linux
                    // .NET ci rimette quello mandato (128), e ogni host sembrava Windows
                    if (OperatingSystem.IsWindows() && reply.Options is { Ttl: > 0 })
                    {
                        ttl = reply.Options.Ttl;
                    }

                    break;
                }

                if (attempt.FailedHere)
                {
                    local++;
                }
            }
        }
        finally
        {
            // la prima risposta basta: i tentativi che aspettano il loro turno non partono, quelli
            // in volo si fermano (e restituiscono il loro posto nel cancello)
            answered.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool online = roundTrips.Count > 0;

        if (online && ttl == 0 && !OperatingSystem.IsWindows())
        {
            ttl = await ReadTtlAsync(target, timeout, cancellationToken).ConfigureAwait(false);
        }

        return new PingOutcome(online, roundTrips, ttl, Math.Max(1, sent), roundTrips.Count)
        {
            Inconclusive = !online && sent > 0 && local == sent
        };
    }

    /// <summary>
    /// Un esito che non dice nulla dell'host, solo del computer che fa il ping: risorse finite,
    /// opzioni o pacchetti rifiutati, errori del driver.
    /// </summary>
    public static bool IsLocalFailure(IPStatus status) => status switch
    {
        IPStatus.Success => false,
        IPStatus.TimedOut => false,
        IPStatus.DestinationHostUnreachable => false,
        IPStatus.DestinationNetworkUnreachable => false,
        IPStatus.DestinationUnreachable => false,
        IPStatus.DestinationPortUnreachable => false,
        // DestinationProhibited (IPv6) ha lo stesso valore: e' compreso qui
        IPStatus.DestinationProtocolUnreachable => false,
        IPStatus.TtlExpired => false,
        IPStatus.TtlReassemblyTimeExceeded => false,
        IPStatus.TimeExceeded => false,
        _ => true
    };

    /// <summary>
    /// Un errore del sistema durante il ping: e' la rete che ha risposto (host o rete
    /// irraggiungibile, rifiutato, nome inesistente) o e' questo computer che non ce l'ha fatta?
    /// </summary>
    public static bool IsLocalFailure(Exception problem)
    {
        for (Exception? inner = problem; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException socket)
            {
                return socket.SocketErrorCode is not (SocketError.ConnectionRefused or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable or SocketError.HostDown or SocketError.NetworkDown
                    or SocketError.TimedOut or SocketError.HostNotFound or SocketError.NoData);
            }
        }

        return true;
    }

    /// <summary>Il TTL vero di un host che ha risposto (macOS, Linux), passando dallo stesso cancello dei ping.</summary>
    private static async Task<int> ReadTtlAsync(string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await IcmpTtl.ReadAsync(target, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Un tentativo, dopo <paramref name="wait"/>: se e' partito, la risposta, e se a fallire e' stato questo computer.</summary>
    private static async Task<Attempt> TryAsync(string target, TimeSpan timeout, TimeSpan wait, CancellationToken token)
    {
        try
        {
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, token).ConfigureAwait(false);
            }

            await Gate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Skipped;
        }

        try
        {
            using Ping ping = new();
            PingOptions options = new() { DontFragment = true };
            PingReply reply = await ping.SendPingAsync(target, timeout, Payload, options, token).ConfigureAwait(false);

            return new Attempt(true, reply, IsLocalFailure(reply.Status));
        }
        catch (OperationCanceledException)
        {
            return Skipped;
        }
        catch (Exception problem) when (problem is PingException or InvalidOperationException or ArgumentException or NotSupportedException or SocketException)
        {
            return new Attempt(true, null, IsLocalFailure(problem));
        }
        finally
        {
            Gate.Release();
        }
    }
}
