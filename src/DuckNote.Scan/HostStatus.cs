namespace DuckNote.Scan;

public enum HostStatus
{
    Online = 0,

    IcmpFiltered = 1,

    Unreachable = 2,
    Error = 3
}

public static class HostStatusText
{
    public static string Describe(HostStatus status) => status switch
    {
        HostStatus.Online => "Online",
        HostStatus.IcmpFiltered => "Online (ICMP filtrato)",
        HostStatus.Unreachable => "Non raggiungibile",
        HostStatus.Error => "Errore",
        _ => string.Empty
    };
}
