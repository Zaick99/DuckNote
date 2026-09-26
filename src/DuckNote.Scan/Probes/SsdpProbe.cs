using System.Text;
using System.Text.RegularExpressions;

namespace DuckNote.Scan.Probes;

public sealed record SsdpFindings(string Server, string Location, string Device, string SearchTarget)
{
    public static readonly SsdpFindings Empty = new("", "", "", "");
}

public static partial class SsdpProbe
{
    private const int Port = 1900;

    public static async Task<SsdpFindings> AskAsync(
        string address, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        string search =
            $"M-SEARCH * HTTP/1.1\r\nHOST: {address}:{Port}\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: ssdp:all\r\n\r\n";

        byte[]? reply = await UdpExchange
            .AskAsync(address, Port, Encoding.ASCII.GetBytes(search), timeout, cancellationToken)
            .ConfigureAwait(false);

        if (reply is null)
        {
            return SsdpFindings.Empty;
        }

        SsdpFindings found = ReadReply(Encoding.ASCII.GetString(reply));
        if (!found.Location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !found.Location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return found;
        }

        string device = await ReadDescriptionAsync(found.Location, cancellationToken).ConfigureAwait(false);
        return found with { Device = device };
    }

    public static SsdpFindings ReadReply(string reply) => new(
        Server: Capture(ServerHeader(), reply, 140),
        Location: Capture(LocationHeader(), reply, 200),
        Device: string.Empty,
        SearchTarget: Capture(SearchTargetHeader(), reply, 100));

    public static string ReadDescription(string xml)
    {
        string[] parts =
        [
            Capture(FriendlyName(), xml, 80),
            Capture(Manufacturer(), xml, 60),
            Capture(ModelName(), xml, 60)
        ];

        return string.Join(" / ", parts.Where(part => part.Length > 0));
    }

    private static async Task<string> ReadDescriptionAsync(string location, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(1500));

        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "DuckNote/2.0");
            string xml = await client.GetStringAsync(location, deadline.Token).ConfigureAwait(false);
            return ReadDescription(xml);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                      or InvalidOperationException or UriFormatException)
        {
            return string.Empty;
        }
    }

    private static string Capture(Regex pattern, string text, int limit)
    {
        Match match = pattern.Match(text);
        return match.Success ? TextSanitiser.Clean(match.Groups[1].Value, limit) : string.Empty;
    }

    [GeneratedRegex(@"(?im)^SERVER:\s*(.+?)\s*$")]
    private static partial Regex ServerHeader();

    [GeneratedRegex(@"(?im)^LOCATION:\s*(.+?)\s*$")]
    private static partial Regex LocationHeader();

    [GeneratedRegex(@"(?im)^ST:\s*(.+?)\s*$")]
    private static partial Regex SearchTargetHeader();

    [GeneratedRegex("(?is)<friendlyName>(.*?)</friendlyName>")]
    private static partial Regex FriendlyName();

    [GeneratedRegex("(?is)<manufacturer>(.*?)</manufacturer>")]
    private static partial Regex Manufacturer();

    [GeneratedRegex("(?is)<modelName>(.*?)</modelName>")]
    private static partial Regex ModelName();
}
