using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed partial class MarkdownRenderer(EditorBrushes brushes, Func<IReadOnlyDictionary<string, bool>> hostStates)
{
    public const string MonoFonts = "SF Mono, Cascadia Mono, Consolas, Menlo, Courier New";

    private static readonly Regex Inline = new(
        @"(?<link>\[[^\]\r\n]*\]\([^)\r\n]*\))" +
        @"|(?<bold>\*\*[^\*\r\n]+?\*\*)|(?<under>__[^_\r\n]+?__)|(?<strike>~~[^~\r\n]+?~~)|(?<mark>==[^=\r\n]+?==)" +
        @"|(?<italic>\*[^\*\r\n]+?\*)|(?<italic2>_[^_\r\n]+?_)|(?<code>`[^`\r\n]+?`)|(?<url>(?:https?://|www\.)[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly DependencyProperty[] Dressing =
    [
        TextElement.FontSizeProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStyleProperty,
        TextElement.FontFamilyProperty,
        TextElement.ForegroundProperty,
        TextElement.BackgroundProperty,
        Block.BorderBrushProperty,
        Block.BorderThicknessProperty,
        Block.PaddingProperty,
        Paragraph.TextIndentProperty
    ];

    public bool LiveFormatting { get; set; } = true;

    public EditorBrushes Palette => brushes;

    public Paragraph CodeBlock(IEnumerable<string> lines)
    {
        Paragraph block = new();
        bool first = true;

        foreach (string line in lines)
        {
            if (!first)
            {
                block.Inlines.Add(new LineBreak());
            }

            block.Inlines.Add(new Run(line));
            first = false;
        }

        if (first)
        {
            block.Inlines.Add(new Run(string.Empty));
        }

        Dress(block, Look.Code);

        return block;
    }

    public void Render(Paragraph row, string line)
    {
        row.Inlines.Clear();
        Undress(row);

        if (!LiveFormatting)
        {
            if (line.Length > 0)
            {
                row.Inlines.Add(Plain(line));
            }

            return;
        }

        ParsedLine parsed = LineParser.Parse(line);

        switch (parsed.Kind)
        {
            case LineKind.Empty:
                break;

            case LineKind.Comment:
                Run comment = Plain(parsed.Raw, brushes.Tertiary);
                comment.FontStyle = FontStyles.Italic;
                row.Inlines.Add(comment);
                break;

            case LineKind.Quote:
                Content(row, parsed.Body);
                Dress(row, Look.Quote);
                break;

            case LineKind.Rule:
                Dress(row, Look.Rule);
                break;

            case LineKind.Todo:
                Content(row, parsed.Body);
                Dress(row, Look.Todo);
                if (parsed.Done)
                {
                    Check(row, done: true);
                }
                break;

            default:
                Line(row, parsed.Raw);
                break;
        }
    }

    private void Line(Paragraph row, string text)
    {
        Match heading = HeadingPattern().Match(text);
        if (heading.Success)
        {
            Content(row, heading.Groups[2].Value);
            Dress(row, (Look)((int)Look.Heading1 + heading.Groups[1].Value.Length - 1));
            return;
        }

        Match list = ListPattern().Match(text);
        if (list.Success)
        {
            Group counted = list.Groups["number"];
            Content(row, list.Groups["body"].Value);
            Dress(row, counted.Success ? Look.Number : Look.Bullet, counted.Success ? int.Parse(counted.Value) : 1);
            return;
        }

        Content(row, text);
    }

    public void Dress(Paragraph row, Look look, int number = 1)
    {
        Undress(row);

        switch (look)
        {
            case Look.Heading1 or Look.Heading2 or Look.Heading3:
                row.FontSize = Looks.HeadingSizes[look - Look.Heading1];
                row.FontWeight = FontWeights.SemiBold;
                break;

            case Look.Quote:
                row.BorderBrush = brushes.Tertiary;
                row.BorderThickness = new Thickness(2, 0, 0, 0);
                row.Padding = Looks.Indent(Look.Quote);
                row.FontStyle = FontStyles.Italic;
                row.Foreground = brushes.Secondary;
                break;

            case Look.Rule:
                row.Inlines.Clear();
                row.BorderBrush = brushes.TableLine;
                row.BorderThickness = new Thickness(0, 0, 0, 1);
                row.Padding = Looks.Indent(Look.Rule);
                break;

            case Look.Bullet:
                Mark(row, Looks.Dot);
                break;

            case Look.Number:
                Mark(row, Looks.Counted(number));
                break;

            case Look.Todo:
                Mark(row, Looks.Unchecked);
                break;

            case Look.Code:
                row.Background = brushes.CodeBackground;
                row.Foreground = brushes.CodeForeground;
                row.FontFamily = new FontFamily(MonoFonts);
                row.Padding = Looks.Indent(Look.Code);
                break;

            default:
                break;
        }
    }

    public void Undress(Paragraph row)
    {
        int mark = Looks.MarkLength(row);
        if (mark > 0)
        {
            Strip(row, mark);
        }

        foreach (DependencyProperty property in Dressing)
        {
            row.ClearValue(property);
        }
    }

    public void Check(Paragraph row, bool done)
    {
        if (!Looks.Is(row, Look.Todo))
        {
            return;
        }

        Strip(row, 2);
        Mark(row, done ? Looks.Checked : Looks.Unchecked);

        bool first = true;
        foreach (Run run in row.Inlines.OfType<Run>())
        {
            if (first)
            {
                run.Foreground = done ? brushes.Up : brushes.Secondary;
                first = false;
                continue;
            }

            run.TextDecorations = done ? TextDecorations.Strikethrough : null;
            run.Foreground = done ? brushes.Done : brushes.Text;
        }
    }

    private void Mark(Paragraph row, string mark)
    {
        Run sign = Plain(mark, brushes.Tertiary);
        sign.FontFamily = new FontFamily(MonoFonts);
        sign.Cursor = Cursors.Hand;

        if (row.Inlines.FirstInline is { } first)
        {
            row.Inlines.InsertBefore(first, sign);
        }
        else
        {
            row.Inlines.Add(sign);
        }

        row.Padding = Looks.Indent(Look.Bullet);
        row.TextIndent = -Looks.Indent(Look.Bullet).Left;
    }

    private static void Strip(Paragraph row, int length)
    {
        if (row.Inlines.FirstInline is not Run first)
        {
            return;
        }

        if (first.Text.Length <= length)
        {
            row.Inlines.Remove(first);
        }
        else
        {
            first.Text = first.Text[length..];
        }

        row.ClearValue(Paragraph.TextIndentProperty);
    }

    private void Content(Paragraph row, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int last = 0;

        foreach (Match match in Inline.Matches(text))
        {
            if (match.Index > last)
            {
                AddText(row, text[last..match.Index]);
            }

            AddStyled(row, match);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            AddText(row, text[last..]);
        }
    }

    private void AddStyled(Paragraph row, Match match)
    {
        string whole = match.Value;

        if (match.Groups["link"].Success)
        {
            int split = whole.IndexOf("](", StringComparison.Ordinal);
            Run label = Plain(whole[1..split]);
            Link(label);
            label.ToolTip = whole[(split + 2)..^1];
            row.Inlines.Add(label);
            return;
        }

        bool code = match.Groups["code"].Success;

        (int markers, Action<Run> style) = match switch
        {
            _ when match.Groups["bold"].Success => (2, (Action<Run>)Bold),
            _ when match.Groups["under"].Success => (2, Underline),
            _ when match.Groups["strike"].Success => (2, Strike),
            _ when match.Groups["mark"].Success => (2, Mark),
            _ when match.Groups["italic"].Success => (1, Italic),
            _ when match.Groups["italic2"].Success => (1, Italic),
            _ when code => (1, Code),
            _ => (0, Link)
        };

        string inner = markers > 0 ? whole[markers..^markers] : whole;

        if (markers == 0 || code)
        {
            Run run = Plain(inner);
            style(run);
            row.Inlines.Add(run);
            return;
        }

        AddText(row, inner, style);
    }

    private void AddText(Paragraph row, string text, Action<Run>? style = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        IReadOnlyDictionary<string, bool> states = hostStates();

        void Emit(Run run, string? host)
        {
            style?.Invoke(run);

            if (host is not null)
            {
                run.Foreground = brushes.ForHost(host, states);
                run.FontFamily = new FontFamily(MonoFonts);
                run.Tag = HostPaint.Mark + host;
            }

            row.Inlines.Add(run);
        }

        int last = 0;
        foreach (HostToken token in HostTokens.Find(text))
        {
            if (token.Start > last)
            {
                Emit(Plain(text[last..token.Start]), host: null);
            }

            Emit(Plain(token.Value), token.Value);
            last = token.Start + token.Length;
        }

        if (last < text.Length)
        {
            Emit(Plain(text[last..]), host: null);
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

    [GeneratedRegex(@"^(#{1,3})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*(?:[-*+]|(?<number>\d+)[.)])\s+(?<body>.*)$")]
    private static partial Regex ListPattern();
}
