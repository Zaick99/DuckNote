using System.Text.RegularExpressions;

namespace DuckNote.Scan.Probes;

public sealed record HttpFindings
{
    public string Title { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string PoweredBy { get; init; } = string.Empty;
    public string Redirect { get; init; } = string.Empty;
    public string TlsSubject { get; init; } = string.Empty;
    public string TlsIssuer { get; init; } = string.Empty;
    public string TlsExpiry { get; init; } = string.Empty;
    public string TlsProtocol { get; init; } = string.Empty;
    public string TlsSubjectAlternativeNames { get; init; } = string.Empty;

    public static readonly HttpFindings Empty = new();

    public string DescribeServer() =>
        string.Join(" / ", new[] { Server, PoweredBy }.Where(part => part.Length > 0));
}

public static partial class HttpResponseReader
{
    public static HttpFindings Read(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return HttpFindings.Empty;
        }

        string title = ReadTitle(response);
        if (title.Length == 0)
        {
            Match realm = AuthenticationRealm().Match(response);
            if (realm.Success)
            {
                title = "realm: " + TextSanitiser.Clean(realm.Groups[1].Value, 80);
            }
        }

        return new HttpFindings
        {
            Code = First(StatusLine(), response, 0),
            Server = First(ServerHeader(), response, 120),
            PoweredBy = First(PoweredByHeader(), response, 120),
            Redirect = First(LocationHeader(), response, 160),
            Title = title
        };
    }

    private static string ReadTitle(string response)
    {
        Match title = TitleElement().Match(response);
        return title.Success
            ? TextSanitiser.Clean(Tags().Replace(title.Groups[1].Value, string.Empty), 160)
            : string.Empty;
    }

    private static string First(Regex pattern, string text, int limit)
    {
        Match match = pattern.Match(text);
        if (!match.Success)
        {
            return string.Empty;
        }
        return limit > 0 ? TextSanitiser.Clean(match.Groups[1].Value, limit) : match.Groups[1].Value;
    }

    [GeneratedRegex(@"^HTTP/[\d\.]+\s+(\d{3})")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"(?im)^Server:\s*(.+?)\s*$")]
    private static partial Regex ServerHeader();

    [GeneratedRegex(@"(?im)^X-Powered-By:\s*(.+?)\s*$")]
    private static partial Regex PoweredByHeader();

    [GeneratedRegex(@"(?im)^Location:\s*(.+?)\s*$")]
    private static partial Regex LocationHeader();

    [GeneratedRegex(@"(?is)<title[^>]*>(.*?)</title>")]
    private static partial Regex TitleElement();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"(?im)^WWW-Authenticate:\s*.*realm=""([^""]+)""")]
    private static partial Regex AuthenticationRealm();
}
