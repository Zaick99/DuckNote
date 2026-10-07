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

    // Il link Markdown viene per primo: dentro un indirizzo ci sta di tutto,
    // trattini bassi compresi, e non deve diventare corsivo per sbaglio.
    private static readonly Regex Inline = new(
        @"(?<link>\[[^\]\r\n]*\]\([^)\r\n]*\))" +
        @"|(?<bold>\*\*[^\*\r\n]+?\*\*)|(?<under>__[^_\r\n]+?__)|(?<strike>~~[^~\r\n]+?~~)|(?<mark>==[^=\r\n]+?==)" +
        @"|(?<italic>\*[^\*\r\n]+?\*)|(?<italic2>_[^_\r\n]+?_)|(?<code>`[^`\r\n]+?`)|(?<url>(?:https?://|www\.)[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Heading = new(@"^(#{1,3})(\s+)(.*)$", RegexOptions.Compiled);

    // Il punto di un elenco e il numero di una numerata: marcatori come gli
    // altri, e come gli altri vanno smorzati invece di leggersi come testo.
    private static readonly Regex ListMarker = new(@"^(\s*(?:[-*+]|\d+[.)])\s+)", RegexOptions.Compiled);

    public bool LiveFormatting { get; set; } = true;

    /// <param name="inCode">
    /// La riga sta fra due marcatori di recinto: dentro un blocco di codice non
    /// si cerca Markdown, si mostra il testo com'e'.
    /// </param>
    public void Render(Paragraph paragraph, string line, bool inCode = false)
    {
        paragraph.TextDecorations = null;
        paragraph.Background = null;
        paragraph.BorderThickness = default;
        paragraph.Padding = default;

        if (!LiveFormatting)
        {
            if (!string.IsNullOrEmpty(line))
            {
                paragraph.Inlines.Add(Plain(line));
            }
            return;
        }

        if (inCode && !LineParser.IsFence(line))
        {
            RenderCode(paragraph, line);
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
                RenderRule(paragraph, parsed);
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

    /// <summary>
    /// Una riga di separazione si vede come una separazione: il taglio lo
    /// disegna il bordo del paragrafo, e i tre meno restano nel testo — piccoli
    /// e smorti, come ogni altro marcatore che la nota non nasconde.
    /// </summary>
    private void RenderRule(Paragraph paragraph, ParsedLine parsed)
    {
        Run dashes = Plain(parsed.Raw, brushes.Tertiary);
        dashes.FontSize = 9;
        paragraph.Inlines.Add(dashes);

        paragraph.BorderBrush = brushes.TableLine;
        paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
        paragraph.Padding = new Thickness(0, 0, 0, 7);
    }

    /// <summary>Una riga dentro il recinto: monospaziata, sul fondo del codice.</summary>
    private void RenderCode(Paragraph paragraph, string line)
    {
        paragraph.Background = brushes.CodeBackground;

        Run body = Plain(line, brushes.CodeForeground);
        body.FontFamily = new FontFamily(MonoFonts);
        paragraph.Inlines.Add(body);
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

        Match list = ListMarker.Match(text);
        if (list.Success)
        {
            paragraph.Inlines.Add(Plain(list.Groups[1].Value, brushes.Tertiary));
            text = text[list.Length..];
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

        if (match.Groups["link"].Success)
        {
            RenderLink(paragraph, whole);
            return;
        }

        bool code = match.Groups["code"].Success;

        (int markerLength, Action<Run> style) = match switch
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

        if (markerLength > 0)
        {
            paragraph.Inlines.Add(Plain(whole[..markerLength], brushes.Tertiary));
        }

        string inner = markerLength > 0
            ? whole.Substring(markerLength, whole.Length - (2 * markerLength))
            : whole;

        if (markerLength == 0 || code)
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

    /// <summary>
    /// `[testo](indirizzo)`: il testo si legge come un link, l'indirizzo resta
    /// visibile e smorto — e' quello che il pulsante del link lascia da
    /// riempire.
    /// </summary>
    private void RenderLink(Paragraph paragraph, string whole)
    {
        int split = whole.IndexOf("](", StringComparison.Ordinal);

        paragraph.Inlines.Add(Plain("[", brushes.Tertiary));

        Run label = Plain(whole[1..split]);
        Link(label);
        paragraph.Inlines.Add(label);

        paragraph.Inlines.Add(Plain(whole[split..], brushes.Tertiary));
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
