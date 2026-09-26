using System.Text.RegularExpressions;

namespace DuckNote.App.Editor;

public enum LineKind
{
    Empty,
    Comment,
    Quote,
    Rule,
    Fence,
    Todo,
    Text
}

public sealed record ParsedLine(LineKind Kind)
{
    public string Raw { get; init; } = string.Empty;
    public string Marker { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public bool Done { get; init; }
    public int Indent { get; init; }
}

public static partial class LineParser
{
    public const char Dot = '●';

    public static ParsedLine Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new ParsedLine(LineKind.Empty);
        }

        string work = line.TrimStart();
        if (work.StartsWith(Dot))
        {
            work = work[1..].TrimStart();
        }

        if (work.StartsWith(';'))
        {
            return new ParsedLine(LineKind.Comment) { Raw = line.TrimStart() };
        }

        Match quote = QuotePattern().Match(work);
        if (quote.Success)
        {
            return new ParsedLine(LineKind.Quote)
            {
                Marker = quote.Groups[1].Value,
                Body = quote.Groups[2].Value
            };
        }

        if (RulePattern().IsMatch(work))
        {
            return new ParsedLine(LineKind.Rule) { Raw = work };
        }

        Match fence = FencePattern().Match(work);
        if (fence.Success)
        {
            return new ParsedLine(LineKind.Fence) { Raw = work, Language = fence.Groups[2].Value.Trim() };
        }

        Match todo = TodoPattern().Match(work);
        if (todo.Success)
        {
            return new ParsedLine(LineKind.Todo)
            {
                Done = todo.Groups[1].Value != " ",
                Body = todo.Groups[2].Value,
                Indent = line.Length - line.TrimStart().Length
            };
        }

        return new ParsedLine(LineKind.Text) { Raw = line };
    }

    [GeneratedRegex(@"^(>+)\s?(.*)$")]
    private static partial Regex QuotePattern();

    [GeneratedRegex(@"^(-{3,}|_{3,}|\*{3,})\s*$")]
    private static partial Regex RulePattern();

    [GeneratedRegex("^(```|~~~)(.*)$")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^[-*+]\s+\[([ xX])\]\s?(.*)$")]
    private static partial Regex TodoPattern();
}
