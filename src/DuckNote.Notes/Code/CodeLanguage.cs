namespace DuckNote.Notes.Code;

/// <summary>Le lingue che un blocco di codice sa riconoscere da solo.</summary>
public enum CodeLanguage
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
/// Che lingua parla un blocco. Non c'e' un posto dove scriverlo, quindi la si indovina dai
/// segni che ogni lingua lascia in giro: un "def ", un "fn ", un "$nome", un SELECT. Nel
/// dubbio nessuna lingua: colorare a caso e' peggio che non colorare.
/// </summary>
public static class CodeLanguages
{
    private const int Enough = 2;

    private static readonly (CodeLanguage Language, string[] Signs)[] Signs =
    [
        (CodeLanguage.CSharp, ["using System", "namespace ", "public class", "private ", "void ", "string ", "=> ", "var "]),
        (CodeLanguage.Python, ["def ", "import ", "elif ", "self.", "print(", "__init__", "None", "True"]),
        (CodeLanguage.JavaScript, ["function ", "const ", "let ", "=>", "console.", "require(", "export ", "null"]),
        (CodeLanguage.Rust, ["fn ", "let mut", "impl ", "pub ", "->", "::", "&str", "match "]),
        (CodeLanguage.PowerShell, ["$", "Get-", "Set-", "param(", "-eq ", "Write-", "foreach (", ".ps1"]),
        (CodeLanguage.Sql, ["select ", "from ", "where ", "join ", "insert into", "update ", "group by", "order by"]),
        (CodeLanguage.Shell, ["#!/bin/", "echo ", "fi\n", "done\n", "grep ", "chmod ", "sudo ", "mkdir "])
    ];

    private static readonly Dictionary<CodeLanguage, IReadOnlySet<string>> Vocabulary = new()
    {
        [CodeLanguage.Plain] = Words(string.Empty, StringComparer.Ordinal),
        [CodeLanguage.CSharp] = Words(
            "abstract async await base bool break byte case catch char class const continue decimal default " +
            "delegate do double else enum event explicit extern false finally fixed float for foreach get goto " +
            "if implicit in int interface internal is lock long namespace new null object operator out override " +
            "params private protected public readonly record ref return sealed set short sizeof static string " +
            "struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void " +
            "volatile when where while yield", StringComparer.Ordinal),
        [CodeLanguage.Python] = Words(
            "and as assert async await break class continue def del elif else except False finally for from " +
            "global if import in is lambda None nonlocal not or pass raise return self True try while with yield",
            StringComparer.Ordinal),
        [CodeLanguage.JavaScript] = Words(
            "async await break case catch class const continue debugger default delete do else export extends " +
            "false finally for from function if import in instanceof let new null of return static super switch " +
            "this throw true try typeof undefined var void while with yield", StringComparer.Ordinal),
        [CodeLanguage.Rust] = Words(
            "as async await break const continue crate dyn else enum extern false fn for if impl in let loop " +
            "match mod move mut pub ref return self Self static struct super trait true type unsafe use where while",
            StringComparer.Ordinal),

        // Lingue che non distinguono le maiuscole: SELECT e select sono la stessa parola.
        [CodeLanguage.PowerShell] = Words(
            "begin break catch continue do dynamicparam else elseif end exit filter finally for foreach from " +
            "function if in param process return switch throw trap try until while", StringComparer.OrdinalIgnoreCase),
        [CodeLanguage.Sql] = Words(
            "add all alter and as asc between by case cast create delete desc distinct drop else end exists " +
            "from full group having in inner insert into is join left like limit not null on or order outer " +
            "primary right select set table then union update values when where with", StringComparer.OrdinalIgnoreCase),
        [CodeLanguage.Shell] = Words(
            "case do done elif else esac fi for function if in local read return select then until while",
            StringComparer.Ordinal)
    };

    public static CodeLanguage Guess(IEnumerable<string> lines)
    {
        string code = string.Join('\n', lines);

        if (code.Trim().Length == 0)
        {
            return CodeLanguage.Plain;
        }

        (CodeLanguage best, int most) = (CodeLanguage.Plain, 0);

        foreach ((CodeLanguage language, string[] signs) in Signs)
        {
            int score = signs.Count(sign => code.Contains(sign, StringComparison.OrdinalIgnoreCase));

            if (score > most)
            {
                (best, most) = (language, score);
            }
        }

        return most >= Enough ? best : CodeLanguage.Plain;
    }

    public static IReadOnlySet<string> Keywords(CodeLanguage language) => Vocabulary[language];

    public static string[] LineComment(CodeLanguage language) => language switch
    {
        CodeLanguage.Python or CodeLanguage.PowerShell or CodeLanguage.Shell => ["#"],
        CodeLanguage.Sql => ["--"],
        _ => ["//"]
    };

    public static bool HasBlockComment(CodeLanguage language) =>
        language is CodeLanguage.CSharp or CodeLanguage.JavaScript or CodeLanguage.Rust or CodeLanguage.Sql;

    public static bool HasTripleQuote(CodeLanguage language) => language is CodeLanguage.Python;

    private static HashSet<string> Words(string words, StringComparer comparer) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), comparer);
}
