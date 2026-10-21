namespace DuckNote.Notes.Security;

/// <summary>
/// Quanto e' lunga una password, in bit: lunghezza per alfabeto usato. Misura la lunghezza,
/// non l'imprevedibilita': "Estate2026!" prende un buon voto e cade in minuti. Usare i dadi.
/// </summary>
public static class PasswordStrength
{
    public static int Bits(ReadOnlySpan<byte> utf8, int characters)
    {
        if (characters == 0)
        {
            return 0;
        }

        bool lower = false, upper = false, digits = false, other = false;

        foreach (byte b in utf8)
        {
            if (b is >= 0x61 and <= 0x7A) { lower = true; }
            else if (b is >= 0x41 and <= 0x5A) { upper = true; }
            else if (b is >= 0x30 and <= 0x39) { digits = true; }
            else { other = true; }
        }

        int alphabet = (lower ? 26 : 0) + (upper ? 26 : 0) + (digits ? 10 : 0) + (other ? 33 : 0);

        return (int)(characters * Math.Log2(Math.Max(2, alphabet)));
    }

    /// <summary>Il token del colore e la parola per la barra.</summary>
    public static (string Token, string Word) Describe(int bits) => bits switch
    {
        0 => ("BorderControl", string.Empty),
        < 40 => ("Red", "fragile"),
        < 60 => ("Yellow", "discreta"),
        < 85 => ("Green", "solida"),
        _ => ("Green", "da manuale")
    };
}
