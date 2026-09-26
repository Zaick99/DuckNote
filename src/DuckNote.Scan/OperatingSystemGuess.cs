using System.Text.RegularExpressions;

namespace DuckNote.Scan;

public static partial class OperatingSystemGuess
{
    public static string Refine(string current, string snmpDescription, string sshBanner)
    {
        if (snmpDescription.Length > 0
            && (current.Length == 0 || !current.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            && KnownSystem().IsMatch(snmpDescription))
        {
            return TextSanitiser.Clean(snmpDescription, 90);
        }

        if (current.Length == 0 && KnownDistribution().IsMatch(sshBanner))
        {
            return TextSanitiser.Clean(sshBanner, 80);
        }

        return current;
    }

    [GeneratedRegex("(?i)(windows|linux|freebsd|ios|junos|routeros|openwrt|vxworks|darwin)")]
    private static partial Regex KnownSystem();

    [GeneratedRegex("(?i)ubuntu|debian|freebsd|openbsd|centos|raspbian")]
    private static partial Regex KnownDistribution();
}
