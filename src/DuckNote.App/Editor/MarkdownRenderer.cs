using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed class MarkdownRenderer(EditorBrushes brushes, Func<IReadOnlyDictionary<string, bool>> hostStates)
{
    public const string MonoFonts = "SF Mono, Cascadia Mono, Consolas, Menlo, Courier New";

    private static readonly int[] HeadingSizes = [21, 17, 15];

    private static readonly Regex Inline = new(
        @"(\*\*[^\*\r\n]+?\*\*)|(__[^_\r\n]+?__)|(~~[^~\r\n]+?~~)|(==[^=\r\n]+?==)" +
        @"|(\*[^\*\r\n]+?\*)|(_[^_\r\n]+?_)|(`[^`\r\n]+?`)|((?:https?://|www\.)[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Heading = new(@"^(#{1,3})(\s+)(.*)$", RegexOptions.Compiled);

    public bool LiveFormatting { get; set; } = true;

    public void Render(Paragraph paragraph, string line)
    {
        paragraph.TextDecorations = null;
        paragraph.Background = null;

        if (!LiveFormatting)
        {
            if (!string.IsNullOrEmpty(line))
            {
                paragraph.Inlines.Add(Plain(line));
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        ParsedLine parsed = LineParser.Parse(line);
        switch (parsed.Kind)
        {
            case LineKind.Comment:
                Run comment = Plain(parsed.Raw, brushes.Tertiary);
                comment.FontStyle = FontStyles.Italic;
                paragraph.Inlines.Add(comment);
                break;

            case LineKind.Quote:
                paragraph.Inlines.Add(Plain(parsed.Marker + " ", brushes.Tertiary));
                Run quoted = Plain(parsed.Body, brushes.Secondary);
                quoted.FontStyle = FontStyles.Italic;
                paragraph.Inlines.Add(quoted);
                break;

            case LineKind.Rule:
                paragraph.Inlines.Add(Plain(parsed.Raw, brushes.Tertiary));
                break;

            case LineKind.Fence:
                Run fence = Plain(parsed.Raw, brushes.Tertiary);
                fence.FontFamily = new FontFamily(MonoFonts);
                paragraph.Inlines.Add(fence);
                paragraph.Background = brushes.CodeBackground;
                break;

            case LineKind.Todo:
                RenderTodo(paragraph, parsed);
                break;

            default:
                RenderInlines(paragraph, parsed.Raw);
                break;
        }
    }

    private void RenderTodo(Paragraph paragraph, ParsedLine parsed)
    {
        string pad = parsed.Indent > 0 ? new string(' ', parsed.Indent) : string.Empty;

        Run box = Plain(pad + (parsed.Done ? "- [x] " : "- [ ] "));
        box.FontFamily = new FontFamily(MonoFonts);
        box.FontWeight = FontWeights.SemiBold;
        box.Foreground = parsed.Done ? brushes.Up : brushes.Secondary;
        box.Tag = "todo";
        box.Cursor = Cursors.Hand;
        paragraph.Inlines.Add(box);

        if (!parsed.Done)
        {
            AddText(paragraph, parsed.Body);
            return;
        }

        Run body = Plain(parsed.Body);
        body.TextDecorations = TextDecorations.Strikethrough;
        body.Foreground = brushes.Done;
        paragraph.Inlines.Add(body);
    }

    private void RenderInlines(Paragraph paragraph, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Match heading = Heading.Match(text);
        if (heading.Success)
        {
            int size = HeadingSizes[heading.Groups[1].Value.Length - 1];

            Run marker = Plain(heading.Groups[1].Value + heading.Groups[2].Value, brushes.Tertiary);
            marker.FontSize = size;
            paragraph.Inlines.Add(marker);

            Run title = Plain(heading.Groups[3].Value);
            title.FontSize = size;
            title.FontWeight = FontWeights.SemiBold;
            paragraph.Inlines.Add(title);
            return;
        }

        int last = 0;
        foreach (Match match in Inline.Matches(text))
        {
            if (match.Index > last)
            {
                AddText(paragraph, text[last..match.Index]);
            }

            AddDecorated(paragraph, match);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            AddText(paragraph, text[last..]);
        }

        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(Plain(text));
        }
    }

    private void AddDecorated(Paragraph paragraph, Match match)
    {
        string whole = match.Value;

        (int markerLength, Action<Run> style) = match switch
        {
            _ when match.Groups[1].Success => (2, (Action<Run>)Bold),
            _ when match.Groups[2].Success => (2, Underline),
            _ when match.Groups[3].Success => (2, Strike),
            _ when match.Groups[4].Success => (2, Mark),
            _ when match.Groups[5].Success => (1, Italic),
            _ when match.Groups[6].Success => (1, Italic),
            _ when match.Groups[7].Success => (1, Code),
            _ => (0, Link)
        };

        if (markerLength > 0)
        {
            paragraph.Inlines.Add(Plain(whole[..markerLength], brushes.Tertiary));
        }

        string inner = markerLength > 0
            ? whole.Substring(markerLength, whole.Length - (2 * markerLength))
            : whole;

        if (markerLength == 0 || match.Groups[7].Success)
        {
            Run run = Plain(inner);
            style(run);
            paragraph.Inlines.Add(run);
        }
        else
        {
            AddText(paragraph, inner, style);
        }

        if (markerLength > 0)
        {
            paragraph.Inlines.Add(Plain(whole[^markerLength..], brushes.Tertiary));
        }
    }

    private void AddText(Paragraph paragraph, string text, Action<Run>? style = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        IReadOnlyDictionary<string, bool> states = hostStates();

        void Emit(Run run, bool isHost, string name)
        {
            style?.Invoke(run);
            if (isHost)
            {
                run.Foreground = brushes.ForHost(name, states);
                run.FontFamily = new FontFamily(MonoFonts);
                run.Tag = "host:" + name;
            }
            paragraph.Inlines.Add(run);
        }

        int last = 0;
        foreach (HostToken token in HostTokens.Find(text))
        {
            if (token.Start > last)
            {
                Emit(Plain(text[last..token.Start]), isHost: false, string.Empty);
            }
            Emit(Plain(token.Value), isHost: true, token.Value);
            last = token.Start + token.Length;
        }

        if (last < text.Length)
        {
            Emit(Plain(text[last..]), isHost: false, string.Empty);
        }
    }

    private Run Plain(string text, Brush? colour = null) =>
        new(text) { Foreground = colour ?? brushes.Text };

    private static void Bold(Run run) => run.FontWeight = FontWeights.Bold;

    private static void Italic(Run run) => run.FontStyle = FontStyles.Italic;

    private static void Underline(Run run) => run.TextDecorations = TextDecorations.Underline;

    private void Strike(Run run)
    {
        run.TextDecorations = TextDecorations.Strikethrough;
        run.Foreground = brushes.Secondary;
    }

    private void Mark(Run run)
    {
        run.Background = brushes.MarkBackground;
        run.Foreground = brushes.MarkForeground;
    }

    private void Link(Run run)
    {
        run.Foreground = brushes.Link;
        run.TextDecorations = TextDecorations.Underline;
    }

    private void Code(Run run)
    {
        run.FontFamily = new FontFamily(MonoFonts);
        run.Background = brushes.CodeBackground;
        run.Foreground = brushes.CodeForeground;
    }
}
