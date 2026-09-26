using System.Collections.Frozen;

namespace DuckNote.Scan;

public static class ServiceNames
{
    private static readonly FrozenDictionary<int, string> Known = new Dictionary<int, string>
    {
        [20] = "ftp-data",     [21] = "ftp",           [22] = "ssh",          [23] = "telnet",
        [25] = "smtp",         [53] = "dns",           [67] = "dhcp",         [69] = "tftp",
        [80] = "http",         [88] = "kerberos",      [110] = "pop3",        [111] = "rpcbind",
        [123] = "ntp",         [135] = "msrpc",        [137] = "netbios-ns",  [139] = "netbios-ssn",
        [143] = "imap",        [161] = "snmp",         [389] = "ldap",        [443] = "https",
        [445] = "smb",         [465] = "smtps",        [514] = "syslog",      [515] = "lpd",
        [548] = "afp",         [554] = "rtsp",         [587] = "submission",  [631] = "ipp",
        [636] = "ldaps",       [873] = "rsync",        [902] = "vmware",      [993] = "imaps",
        [995] = "pop3s",       [1080] = "socks",       [1194] = "openvpn",    [1433] = "mssql",
        [1521] = "oracle",     [1723] = "pptp",        [1883] = "mqtt",       [1900] = "ssdp",
        [2049] = "nfs",        [2082] = "cpanel",      [2222] = "ssh-alt",    [2375] = "docker",
        [3000] = "http-alt",   [3128] = "squid",       [3268] = "gc-ldap",    [3306] = "mysql",
        [3389] = "rdp",        [3690] = "svn",         [4444] = "metasploit", [5000] = "upnp/http",
        [5060] = "sip",        [5222] = "xmpp",        [5353] = "mdns",       [5432] = "postgres",
        [5555] = "adb",        [5601] = "kibana",      [5900] = "vnc",        [5985] = "winrm",
        [5986] = "winrm-tls",  [6379] = "redis",       [6667] = "irc",        [7070] = "realserver",
        [8000] = "http-alt",   [8006] = "proxmox",     [8080] = "http-proxy", [8081] = "http-alt",
        [8123] = "home-assistant", [8443] = "https-alt", [8888] = "http-alt", [9000] = "http-alt",
        [9090] = "cockpit",    [9100] = "jetdirect",   [9200] = "elastic",    [10000] = "webmin",
        [11211] = "memcached", [27017] = "mongodb",    [32400] = "plex",      [49152] = "upnp"
    }.ToFrozenDictionary();

    public static string Describe(int port) =>
        Known.TryGetValue(port, out string? name) ? name : $"tcp/{port}";

    public static string DescribeAll(IEnumerable<int> ports) =>
        string.Join(", ", ports.Select(port => $"{port}/{Describe(port)}"));
}
