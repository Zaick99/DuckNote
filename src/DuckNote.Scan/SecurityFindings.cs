using System.Globalization;

namespace DuckNote.Scan;

public static class SecurityFindings
{
    public static string Describe(IReadOnlyCollection<int> openPorts, string tlsExpiry)
    {
        HashSet<int> ports = [.. openPorts];
        List<string> findings = [];

        if (ports.Contains(23))
        {
            findings.Add("Telnet aperto");
        }
        if (ports.Contains(21))
        {
            findings.Add("FTP aperto");
        }
        if (ports.Contains(3389))
        {
            findings.Add("RDP esposto");
        }
        if (ports.Contains(445) && ports.Contains(139))
        {
            findings.Add("SMB legacy");
        }
        if (IsExpired(tlsExpiry))
        {
            findings.Add("Certificato TLS scaduto");
        }

        return string.Join(" - ", findings);
    }

    private static bool IsExpired(string tlsExpiry) =>
        tlsExpiry.Length > 0
        && DateTime.TryParse(tlsExpiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiry)
        && expiry < DateTime.Now;
}
