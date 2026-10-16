namespace DuckNote.App.Editor;

/// <summary>Le lingue che un blocco di codice sa riconoscere da solo.</summary>
public enum Tongue
{
    Plain,

    CSharp,

    Python,

    JavaScript,

    Rust,

    PowerShell,

    Sql,

    Shell
}

/// <summary>
/// Che lingua parla un blocco di codice. Non c'e' nessun posto dove scriverlo
/// -- i marcatori del Markdown non ci sono piu' -- quindi si indovina dai segni
/// che ogni lingua lascia in giro: un "def ", un "fn ", un "$nome", un SELECT.
/// Nel dubbio nessuna lingua: colorare a caso e' peggio che non colorare.
/// </summary>
public static class CodeTongue
{
    private const int Enough = 2;

    public static Tongue Of(IEnumerable<string> lines)
    {
        string code = string.Join('\n', lines);

        if (code.Trim().Length == 0)
        {
            return Tongue.Plain;
        }

        Tongue best = Tongue.Plain;
        int most = 0;

        foreach ((Tongue tongue, string[] signs) in Signs)
        {
            int score = signs.Count(sign => code.Contains(sign, StringComparison.OrdinalIgnoreCase));

            if (score > most)
            {
                best = tongue;
                most = score;
            }
        }

        return most >= Enough ? best : Tongue.Plain;
    }

    /// <summary>I segni che tradiscono una lingua, anche in poche righe.</summary>
    private static readonly (Tongue Tongue, string[] Signs)[] Signs =
    [
        (Tongue.CSharp, ["using System", "namespace ", "public class", "private ", "void ", "string ", "=> ", "var "]),
        (Tongue.Python, ["def ", "import ", "elif ", "self.", "print(", "__init__", "None", "True"]),
        (Tongue.JavaScript, ["function ", "const ", "let ", "=>", "console.", "require(", "export ", "null"]),
        (Tongue.Rust, ["fn ", "let mut", "impl ", "pub ", "->", "::", "&str", "match "]),
        (Tongue.PowerShell, ["$", "Get-", "Set-", "param(", "-eq ", "Write-", "foreach (", ".ps1"]),
        (Tongue.Sql, ["select ", "from ", "where ", "join ", "insert into", "update ", "group by", "order by"]),
        (Tongue.Shell, ["#!/bin/", "echo ", "fi\n", "done\n", "grep ", "chmod ", "sudo ", "mkdir "])
    ];

    /// <summary>Le parole che la lingua si tiene per se'.</summary>
    public static IReadOnlySet<string> Words(Tongue tongue) => Vocabulary[tongue];

    /// <summary>Come la lingua comincia un commento fino a fine riga.</summary>
    public static string[] LineComment(Tongue tongue) => tongue switch
    {
        Tongue.Python or Tongue.PowerShell or Tongue.Shell => ["#"],
        Tongue.Sql => ["--"],
        _ => ["//"]
    };

    /// <summary>La lingua usa i commenti aperti e chiusi, che passano le righe.</summary>
    public static bool HasBlockComment(Tongue tongue) =>
        tongue is Tongue.CSharp or Tongue.JavaScript or Tongue.Rust or Tongue.Sql;

    /// <summary>La lingua scrive testi lunghi fra tre virgolette.</summary>
    public static bool HasTripleQuote(Tongue tongue) => tongue is Tongue.Python;

    private static readonly Dictionary<Tongue, IReadOnlySet<string>> Vocabulary = new()
    {
        [Tongue.Plain] = new HashSet<string>(StringComparer.Ordinal),

        [Tongue.CSharp] = Set(
            "abstract async await base bool break byte case catch char class const continue decimal default " +
            "delegate do double else enum event explicit extern false finally fixed float for foreach get goto " +
            "if implicit in int interface internal is lock long namespace new null object operator out override " +
            "params private protected public readonly record ref return sealed set short sizeof static string " +
            "struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void " +
            "volatile when where while yield"),

        [Tongue.Python] = Set(
            "and as assert async await break class continue def del elif else except False finally for from " +
            "global if import in is lambda None nonlocal not or pass raise return self True try while with yield"),

        [Tongue.JavaScript] = Set(
            "async await break case catch class const continue debugger default delete do else export extends " +
            "false finally for from function if import in instanceof let new null of return static super switch " +
            "this throw true try typeof undefined var void while with yield"),

        [Tongue.Rust] = Set(
            "as async await break const continue crate dyn else enum extern false fn for if impl in let loop " +
            "match mod move mut pub ref return self Self static struct super trait true type unsafe use where while"),

        // Due lingue che non distinguono le maiuscole: SELECT e select sono la
        // stessa parola, e chi scrive SQL le mescola senza pensarci.
        [Tongue.PowerShell] = Loose(
            "begin break catch continue do dynamicparam else elseif end exit filter finally for foreach from " +
            "function if in param process return switch throw trap try until while"),

        [Tongue.Sql] = Loose(
            "add all alter and as asc between by case cast create delete desc distinct drop else end exists " +
            "from full group having in inner insert into is join left like limit not null on or order outer " +
            "primary right select set table then union update values when where with"),

        [Tongue.Shell] = Set(
            "case do done elif else esac fi for function if in local read return select then until while")
    };

    private static IReadOnlySet<string> Set(string words) => Set(words, StringComparer.Ordinal);

    private static IReadOnlySet<string> Loose(string words) => Set(words, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Set(string words, StringComparer how) =>
        new HashSet<string>(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), how);
}
