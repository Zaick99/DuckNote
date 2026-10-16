namespace DuckNote.App.Editor;

/// <summary>Di che cosa e' fatto un pezzo di codice.</summary>
public enum CodeInk
{
    Plain,

    /// <summary>Una parola che la lingua si tiene per se': if, return, SELECT.</summary>
    Keyword,

    /// <summary>Testo fra virgolette.</summary>
    Quote,

    Number,

    Comment,

    /// <summary>Un nome chiamato o dichiarato come funzione.</summary>
    Call
}

/// <summary>Un pezzo di riga e di che cosa e' fatto.</summary>
public readonly record struct CodePiece(int Start, int Length, CodeInk Ink);

/// <summary>
/// Quel che una riga di codice porta dentro, pezzo per pezzo. Si legge una riga
/// per volta portandosi dietro cio' che era rimasto aperto -- un commento, un
/// testo fra tre virgolette -- perche' le righe di un blocco sono pezzi
/// separati nel documento, e contarle tutte insieme a caratteri, con i fine
/// riga di mezzo, vorrebbe dire sbagliare di uno a ogni riga.
/// </summary>
public static class CodeScan
{
    /// <summary>Quel che una riga lascia aperto per la riga dopo.</summary>
    public enum Carry
    {
        None,

        InComment,

        InQuote
    }

    private const string ShutComment = "*/";

    private const string Triple = "\"\"\"";

    public static CodePiece[] Line(string line, Tongue tongue, ref Carry carry)
    {
        List<CodePiece> pieces = [];
        int at = 0;

        while (at < line.Length)
        {
            at += Math.Max(1, Piece(line, at, tongue, pieces, ref carry));
        }

        return [.. pieces];
    }

    private static int Piece(string line, int at, Tongue tongue, List<CodePiece> pieces, ref Carry carry)
    {
        if (carry == Carry.InComment)
        {
            return Upto(line, at, 0, ShutComment, CodeInk.Comment, pieces, ref carry, Carry.InComment);
        }

        if (carry == Carry.InQuote)
        {
            return Upto(line, at, 0, Triple, CodeInk.Quote, pieces, ref carry, Carry.InQuote);
        }

        if (CodeTongue.HasBlockComment(tongue) && At(line, at, "/*"))
        {
            return Upto(line, at, 2, ShutComment, CodeInk.Comment, pieces, ref carry, Carry.InComment);
        }

        if (CodeTongue.LineComment(tongue).Any(mark => At(line, at, mark)))
        {
            pieces.Add(new CodePiece(at, line.Length - at, CodeInk.Comment));

            return line.Length - at;
        }

        if (CodeTongue.HasTripleQuote(tongue) && At(line, at, Triple))
        {
            return Upto(line, at, 3, Triple, CodeInk.Quote, pieces, ref carry, Carry.InQuote);
        }

        if (line[at] is '"' or '\'')
        {
            return Quote(line, at, pieces);
        }

        if (char.IsAsciiDigit(line[at]) && !Joined(line, at))
        {
            int digits = Run(line, at, here => char.IsAsciiLetterOrDigit(here) || here is '.' or '_');
            pieces.Add(new CodePiece(at, digits, CodeInk.Number));

            return digits;
        }

        if (!Opens(line[at]))
        {
            return 1;
        }

        int length = Run(line, at, Inside);
        pieces.Add(new CodePiece(at, length, Ink(line.Substring(at, length), line, at + length, tongue)));

        return length;
    }

    /// <summary>
    /// Il pezzo va fino al segno che lo chiude, saltando il segno che l'ha
    /// aperto. Se il segno non arriva, il pezzo prende tutta la riga e resta
    /// aperto per la prossima.
    /// </summary>
    private static int Upto(
        string line, int at, int skip, string shut, CodeInk ink,
        List<CodePiece> pieces, ref Carry carry, Carry stays)
    {
        int end = line.IndexOf(shut, Math.Min(at + skip, line.Length), StringComparison.Ordinal);
        int length = end < 0 ? line.Length - at : end + shut.Length - at;

        pieces.Add(new CodePiece(at, length, ink));
        carry = end < 0 ? stays : Carry.None;

        return length;
    }

    /// <summary>
    /// Virgolette semplici, con la barra rovescia che ne annulla una. Mai
    /// chiuse, il testo finisce con la riga: mentre si scrive una riga ne ha
    /// sempre di aperte, e non devono tingere tutto il blocco.
    /// </summary>
    private static int Quote(string line, int at, List<CodePiece> pieces)
    {
        char fence = line[at];
        int walk = at + 1;

        while (walk < line.Length && line[walk] != fence)
        {
            walk += line[walk] == '\\' && walk + 1 < line.Length ? 2 : 1;
        }

        int length = Math.Min(walk + 1, line.Length) - at;
        pieces.Add(new CodePiece(at, length, CodeInk.Quote));

        return length;
    }

    private static CodeInk Ink(string word, string line, int after, Tongue tongue)
    {
        if (CodeTongue.Words(tongue).Contains(word))
        {
            return CodeInk.Keyword;
        }

        // Un nome attaccato a una parentesi aperta e' una funzione: chiamata o
        // dichiarata, per chi legge e' la stessa cosa -- il nome di qualcosa
        // che si fa.
        while (after < line.Length && line[after] == ' ')
        {
            after++;
        }

        return after < line.Length && line[after] == '(' ? CodeInk.Call : CodeInk.Plain;
    }

    private static bool At(string line, int at, string mark) =>
        line.AsSpan(at).StartsWith(mark, StringComparison.Ordinal);

    private static int Run(string line, int at, Func<char, bool> belongs)
    {
        int walk = at;

        while (walk < line.Length && belongs(line[walk]))
        {
            walk++;
        }

        return walk - at;
    }

    private static bool Opens(char here) => char.IsLetter(here) || here is '_' or '$' or '@';

    private static bool Inside(char here) => char.IsLetterOrDigit(here) || here is '_' or '$' or '-';

    /// <summary>Questa cifra sta dentro un nome, non all'inizio di un numero.</summary>
    private static bool Joined(string line, int at) => at > 0 && Inside(line[at - 1]);
}
