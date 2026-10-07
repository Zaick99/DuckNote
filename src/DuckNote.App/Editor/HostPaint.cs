using System.Windows.Documents;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed class HostPaint(EditorBrushes brushes, Func<IReadOnlyDictionary<string, bool>> states)
{
    public const string Mark = "host:";

    public void Paint(Paragraph row)
    {
        foreach (Run run in row.Inlines.OfType<Run>().ToArray())
        {
            Split(row, run);
        }
    }

    public void Recolour(Paragraph row)
    {
        foreach (Run run in row.Inlines.OfType<Run>())
        {
            if (Named(run) is { } host)
            {
                run.Foreground = brushes.ForHost(host, states());
            }
        }
    }

    public static string? Named(Run run) =>
        run.Tag is string tag && tag.StartsWith(Mark, StringComparison.Ordinal) ? tag[Mark.Length..] : null;

    private void Split(Paragraph row, Run run)
    {
        string text = run.Text;

        if (Named(run) is { } already)
        {
            if (already == text)
            {
                run.Foreground = brushes.ForHost(already, states());
                return;
            }

            run.Tag = null;
            run.ClearValue(TextElement.FontFamilyProperty);
            run.Foreground = brushes.Text;
        }

        HostToken[] tokens = [.. HostTokens.Find(text)];
        if (tokens.Length == 0)
        {
            return;
        }

        if (tokens.Length == 1 && tokens[0].Start == 0 && tokens[0].Length == text.Length)
        {
            Wear(run, tokens[0].Value);
            return;
        }

        Inline after = run;
        int last = 0;

        foreach (HostToken token in tokens)
        {
            if (token.Start > last)
            {
                after = Insert(row, after, Copy(run, text[last..token.Start]));
            }

            Run host = Copy(run, token.Value);
            Wear(host, token.Value);
            after = Insert(row, after, host);

            last = token.Start + token.Length;
        }

        if (last < text.Length)
        {
            Insert(row, after, Copy(run, text[last..]));
        }

        row.Inlines.Remove(run);
    }

    private void Wear(Run run, string host)
    {
        run.Tag = Mark + host;
        run.Foreground = brushes.ForHost(host, states());
        run.FontFamily = new FontFamily(MarkdownRenderer.MonoFonts);
    }

    private static Inline Insert(Paragraph row, Inline after, Run piece)
    {
        row.Inlines.InsertAfter(after, piece);

        return piece;
    }

    private static Run Copy(Run from, string text) => new(text)
    {
        FontWeight = from.FontWeight,
        FontStyle = from.FontStyle,
        FontFamily = from.FontFamily,
        FontSize = from.FontSize,
        Foreground = from.Foreground,
        Background = from.Background,
        TextDecorations = from.TextDecorations
    };
}
