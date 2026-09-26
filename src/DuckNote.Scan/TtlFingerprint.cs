namespace DuckNote.Scan;

public static class TtlFingerprint
{
    private static readonly int[] InitialValues = [64, 128, 255, 32];

    public static string GuessOperatingSystem(int ttl) => ttl switch
    {
        <= 0 => string.Empty,
        <= 32 => "Embedded / legacy",
        <= 64 => "Linux / Unix / macOS",
        <= 128 => "Windows",
        <= 255 => "Apparato di rete (Cisco/Solaris)",
        _ => string.Empty
    };

    public static int CountHops(int ttl)
    {
        if (ttl <= 0)
        {
            return 0;
        }

        foreach (int initial in InitialValues)
        {
            if (ttl <= initial)
            {
                return initial - ttl;
            }
        }
        return 0;
    }
}
