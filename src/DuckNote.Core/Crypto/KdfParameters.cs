namespace DuckNote.Core.Crypto;

public sealed record KdfParameters(int MemoryKib, int Passes, int Lanes, int Pbkdf2Iterations)
{
    public static readonly KdfParameters Default = new(
        MemoryKib: 256 * 1024,
        Passes: 3,
        Lanes: 4,
        Pbkdf2Iterations: 600_000);

    private const int MaximumMemoryKib = 1024 * 1024;
    private const int MaximumPasses = 16;
    private const int MaximumLanes = 16;
    private const int MaximumPbkdf2 = 10_000_000;

    public bool IsPlausible =>
        MemoryKib >= 8 * Lanes && MemoryKib <= MaximumMemoryKib
        && Passes is >= 1 and <= MaximumPasses
        && Lanes is >= 1 and <= MaximumLanes
        && Pbkdf2Iterations is >= 1 and <= MaximumPbkdf2;
}
