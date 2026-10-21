namespace DuckNote.Notes.Code;

/// <summary>Di che cosa e' fatto un pezzo di codice.</summary>
public enum CodeInk
{
    Plain,
    Keyword,
    Quote,
    Number,
    Comment,
    Call
}

/// <summary>Un pezzo di riga e il suo colore.</summary>
public readonly record struct CodeSpan(string Text, CodeInk Ink);

/// <summary>Quel che una riga lascia aperto per la successiva.</summary>
public enum CodeCarry
{
    None,
    InComment,
    InQuote
}

/// <summary>
/// Taglia un blocco di codice in pezzi colorati, una riga alla volta, portandosi dietro
/// cio' che e' rimasto aperto: un commento /* ... o un testo fra tre virgolette passano le
/// righe. Una virgoletta semplice no: mentre si scrive una riga ne ha sempre una aperta, e
/// non deve tingere tutto il blocco.
/// </summary>
public static class CodeLexer
{
    private const string ShutComment = "*/";
    private const string Triple = "\"\"\"";

    /// <summary>Le righe del blocco, ciascuna nei suoi pezzi, pezzi vicini dello stesso colore uniti.</summary>
    public static IReadOnlyList<IReadOnlyList<CodeSpan>> Paint(IReadOnlyList<string> lines)
    {
        CodeLanguage language = CodeLanguages.Guess(lines);
        CodeCarry carry = CodeCarry.None;
        List<IReadOnlyList<CodeSpan>> rows = [];

        foreach (string line in lines)
        {
            rows.Add(Line(line, language, ref carry));
        }

        return rows;
    }

    public static IReadOnlyList<CodeSpan> Line(string line, CodeLanguage language, ref CodeCarry carry)
    {
        List<CodeSpan> spans = [];
        int at = 0;

        while (at < line.Length)
        {
            (int length, CodeInk ink) = Next(line, at, language, ref carry);
            length = Math.Max(1, length);
            Add(spans, line.Substring(at, length), ink);
            at += length;
        }

        return spans;
    }

    private static (int Length, CodeInk Ink) Next(string line, int at, CodeLanguage language, ref CodeCarry carry)
    {
        if (carry == CodeCarry.InComment)
        {
            return (Upto(line, at, 0, ShutComment, ref carry, CodeCarry.InComment), CodeInk.Comment);
        }

        if (carry == CodeCarry.InQuote)
        {
            return (Upto(line, at, 0, Triple, ref carry, CodeCarry.InQuote), CodeInk.Quote);
        }

        if (CodeLanguages.HasBlockComment(language) && Starts(line, at, "/*"))
        {
            return (Upto(line, at, 2, ShutComment, ref carry, CodeCarry.InComment), CodeInk.Comment);
        }

        if (CodeLanguages.LineComment(language).Any(mark => Starts(line, at, mark)))
        {
            return (line.Length - at, CodeInk.Comment);
        }

        if (CodeLanguages.HasTripleQuote(language) && Starts(line, at, Triple))
        {
            return (Upto(line, at, 3, Triple, ref carry, CodeCarry.InQuote), CodeInk.Quote);
        }

        char here = line[at];

        if (here is '"' or '\'')
        {
            return (Quoted(line, at), CodeInk.Quote);
        }

        if (char.IsAsciiDigit(here) && !(at > 0 && InWord(line[at - 1])))
        {
            return (Span(line, at, c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_'), CodeInk.Number);
        }

        if (!(char.IsLetter(here) || here is '_' or '$' or '@'))
        {
            return (1, CodeInk.Plain);
        }

        int length = Span(line, at, InWord);
        string word = line.Substring(at, length);

        if (CodeLanguages.Keywords(language).Contains(word))
        {
            return (length, CodeInk.Keyword);
        }

        // Un nome attaccato a una parentesi aperta e' una funzione, chiamata o dichiarata.
        int after = at + length;
        while (after < line.Length && line[after] == ' ')
        {
            after++;
        }

        return (length, after < line.Length && line[after] == '(' ? CodeInk.Call : CodeInk.Plain);
    }

    private static int Upto(string line, int at, int skip, string shut, ref CodeCarry carry, CodeCarry open)
    {
        int end = line.IndexOf(shut, Math.Min(at + skip, line.Length), StringComparison.Ordinal);
        carry = end < 0 ? open : CodeCarry.None;

        return end < 0 ? line.Length - at : end + shut.Length - at;
    }

    private static int Quoted(string line, int at)
    {
        char fence = line[at];
        int walk = at + 1;

        while (walk < line.Length && line[walk] != fence)
        {
            walk += line[walk] == '\\' && walk + 1 < line.Length ? 2 : 1;
        }

        return Math.Min(walk + 1, line.Length) - at;
    }

    private static bool Starts(string line, int at, string mark) =>
        line.AsSpan(at).StartsWith(mark, StringComparison.Ordinal);

    private static int Span(string line, int at, Func<char, bool> belongs)
    {
        int walk = at;

        while (walk < line.Length && belongs(line[walk]))
        {
            walk++;
        }

        return walk - at;
    }

    private static bool InWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '-';

    private static void Add(List<CodeSpan> spans, string text, CodeInk ink)
    {
        if (spans.Count > 0 && spans[^1].Ink == ink)
        {
            spans[^1] = spans[^1] with { Text = spans[^1].Text + text };
            return;
        }

        spans.Add(new CodeSpan(text, ink));
    }
}
