using DuckNote.Scan.Wire;

namespace DuckNote.Scan.Probes;

public static class SnmpProbe
{
    private const int Port = 161;

    public static async Task<IReadOnlyDictionary<string, string>> AskAsync(
        string address, string community, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        byte[] request = SnmpMessage.BuildGet(community, SnmpMessage.SystemGroup, Random.Shared.Next(1, 2_147_483_000));
        byte[]? reply = await UdpExchange.AskAsync(address, Port, request, timeout, cancellationToken)
                                         .ConfigureAwait(false);

        return reply is null
            ? new Dictionary<string, string>()
            : SnmpMessage.ReadResponse(reply, SnmpMessage.SystemGroup);
    }
}
