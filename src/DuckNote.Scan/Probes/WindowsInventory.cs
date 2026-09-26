namespace DuckNote.Scan.Probes;

public sealed record WindowsInventory
{
    public string OperatingSystem { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Serial { get; init; } = string.Empty;
    public string Uptime { get; init; } = string.Empty;
    public string Cpu { get; init; } = string.Empty;
    public string Ram { get; init; } = string.Empty;
    public string Disks { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;

    public static readonly WindowsInventory Empty = new();
}
