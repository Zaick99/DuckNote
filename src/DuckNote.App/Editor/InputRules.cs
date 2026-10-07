using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed partial class InputRules(RichTextBox editor, MarkdownRenderer renderer, CodeBlocks blocks, EditorBrushes brushes)
{
    public bool Apply(Paragraph? row)
    {
        if (row is null || Looks.Is(row, Look.Code))
        {
            return false;
        }

        return Block(row) || Inside(row);
    }

    private bool Block(Paragraph row)
    {
        if (Looks.Of(row) != Look.Text)
        {
            return false;
        }

        string text = Text(row);

        if (CodeOpening().IsMatch(text))
        {
            Paragraph? block = blocks.Fold([row]);
            if (block is null)
            {
                return false;
            }

            Clear(block);
            editor.CaretPosition = block.ContentStart;

            return true;
        }

        if (RuleLine().IsMatch(text))
        {
            Eat(row, text.Length);
            renderer.Dress(row, Look.Rule);
            editor.CaretPosition = row.ContentEnd;

            return true;
        }

        Match opening = Opening().Match(text);
        if (!opening.Success)
        {
            return false;
        }

        (Look look, int number) = Wanted(opening);

        Eat(row, opening.Groups[0].Value.Length);
        renderer.Dress(row, look, number);

        if (look == Look.Todo && opening.Groups["done"].Value is "x" or "X")
        {
            renderer.Check(row, done: true);
        }

        editor.CaretPosition = row.ContentEnd;

        return true;
    }

    private static (Look Look, int Number) Wanted(Match opening)
    {
        if (opening.Groups["hash"].Success)
        {
            return ((Look)((int)Look.Heading1 + opening.Groups["hash"].Value.Length - 1), 1);
        }

        if (opening.Groups["quote"].Success)
        {
            return (Look.Quote, 1);
        }

        if (opening.Groups["done"].Success)
        {
            return (Look.Todo, 1);
        }

        return opening.Groups["number"].Success
            ? (Look.Number, int.Parse(opening.Groups["number"].Value))
            : (Look.Bullet, 1);
    }

    private bool Inside(Paragraph row)
    {
        string text = Text(row);
        Match[] found = [.. Pairs().Matches(text).Cast<Match>().Reverse()];

        if (found.Length == 0)
        {
            return false;
        }

        foreach (Match pair in found)
        {
            Transform(row, pair);
        }

        return true;
    }

    private void Transform(Paragraph row, Match pair)
    {
        if (At(row, pair.Index) is not { } from || At(row, pair.Index + pair.Length) is not { } to)
        {
            return;
        }

        bool link = pair.Groups["link"].Success;
        string inner = link ? Label(pair.Value) : Inner(pair);

        TextRange span = new(from, to);

        try
        {
            span.Text = inner;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach ((DependencyProperty property, object value) in Styling(pair))
        {
            span.ApplyPropertyValue(property, value);
        }

        editor.CaretPosition = span.End;
    }

    private IEnumerable<(DependencyProperty Property, object Value)> Styling(Match pair)
    {
        if (pair.Groups["bold"].Success)
        {
            yield return (TextElement.FontWeightProperty, FontWeights.Bold);
        }
        else if (pair.Groups["italic"].Success || pair.Groups["italic2"].Success)
        {
            yield return (TextElement.FontStyleProperty, FontStyles.Italic);
        }
        else if (pair.Groups["under"].Success)
        {
            yield return (Inline.TextDecorationsProperty, TextDecorations.Underline);
        }
        else if (pair.Groups["strike"].Success)
        {
            yield return (Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            yield return (TextElement.ForegroundProperty, brushes.Secondary);
        }
        else if (pair.Groups["mark"].Success)
        {
            yield return (TextElement.BackgroundProperty, brushes.MarkBackground);
            yield return (TextElement.ForegroundProperty, brushes.MarkForeground);
        }
        else if (pair.Groups["code"].Success)
        {
            yield return (TextElement.FontFamilyProperty, new FontFamily(MarkdownRenderer.MonoFonts));
            yield return (TextElement.BackgroundProperty, brushes.CodeBackground);
            yield return (TextElement.ForegroundProperty, brushes.CodeForeground);
        }
        else
        {
            yield return (TextElement.ForegroundProperty, brushes.Link);
            yield return (Inline.TextDecorationsProperty, TextDecorations.Underline);
        }
    }

    private static string Inner(Match pair)
    {
        int markers = pair.Groups["italic"].Success || pair.Groups["italic2"].Success || pair.Groups["code"].Success ? 1 : 2;

        return pair.Value[markers..^markers];
    }

    private static string Label(string link)
    {
        int split = link.IndexOf("](", StringComparison.Ordinal);

        return split > 0 ? link[1..split] : link;
    }

    private static string Text(Paragraph row) =>
        LiveFormatter.TextOf(row).Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static void Eat(Paragraph row, int length)
    {
        if (At(row, 0) is not { } from || At(row, length) is not { } to)
        {
            return;
        }

        try
        {
            new TextRange(from, to).Text = string.Empty;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void Clear(Paragraph row)
    {
        foreach (Run run in row.Inlines.OfType<Run>().ToArray())
        {
            if (CodeOpening().IsMatch(run.Text))
            {
                run.Text = string.Empty;
            }
        }
    }

    private static TextPointer? At(Paragraph row, int offset)
    {
        TextPointer? at = row.ContentStart;
        int left = offset;

        while (at is not null && left > 0 && at.CompareTo(row.ContentEnd) < 0)
        {
            if (at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                at = at.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int step = Math.Min(at.GetTextInRun(LogicalDirection.Forward).Length, left);
            at = at.GetPositionAtOffset(step);
            left -= step;
        }

        return left == 0 ? at : null;
    }

    [GeneratedRegex(@"^(?:(?<hash>#{1,3})|(?<quote>>)|[-*+]\s+\[(?<done>[ xX])\]|(?<number>\d+)[.)]|[-*+])\s")]
    private static partial Regex Opening();

    [GeneratedRegex(@"^(?:-{3,}|_{3,}|\*{3,})\s*$")]
    private static partial Regex RuleLine();

    [GeneratedRegex("^(?:```|~~~)\\s*$")]
    private static partial Regex CodeOpening();

    [GeneratedRegex(
        @"(?<link>\[[^\]\r\n]*\]\([^)\r\n]*\))" +
        @"|(?<bold>\*\*(?!\s)[^\*\r\n]+?(?<!\s)\*\*)|(?<under>__(?!\s)[^_\r\n]+?(?<!\s)__)" +
        @"|(?<strike>~~(?!\s)[^~\r\n]+?(?<!\s)~~)|(?<mark>==(?!\s)[^=\r\n]+?(?<!\s)==)" +
        @"|(?<italic>\*(?!\s)[^\*\r\n]+?(?<!\s)\*)|(?<italic2>_(?!\s)[^_\r\n]+?(?<!\s)_)" +
        @"|(?<code>`(?!\s)[^`\r\n]+?(?<!\s)`)")]
    private static partial Regex Pairs();
}
