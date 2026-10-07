using System.Windows;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public enum Look
{
    Text,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    Rule,
    Code,
    Bullet,
    Number,
    Todo
}

public static class Looks
{
    public const double Body = 13.5;

    public static readonly double[] HeadingSizes = [21, 17, 15];

    private const string Gap = " ";

    public const string Dot = "●" + Gap;
    public const string Unchecked = "☐" + Gap;
    public const string Checked = "☑" + Gap;

    public static string Counted(int number) => number.ToString() + "." + Gap;

    public static Look Of(Paragraph? row)
    {
        if (row is null)
        {
            return Look.Text;
        }

        if (row.Background is not null)
        {
            return Look.Code;
        }

        if (row.BorderThickness.Bottom > 0)
        {
            return Look.Rule;
        }

        if (row.BorderThickness.Left > 0)
        {
            return Look.Quote;
        }

        if (Heading(row) is { } level)
        {
            return level;
        }

        return Marked(row);
    }

    public static bool Is(Paragraph? row, Look look) => Of(row) == look;

    private static Look? Heading(Paragraph row)
    {
        for (int i = 0; i < HeadingSizes.Length; i++)
        {
            if (Math.Abs(row.FontSize - HeadingSizes[i]) < 0.6)
            {
                return (Look)((int)Look.Heading1 + i);
            }
        }

        return null;
    }

    private static Look Marked(Paragraph row)
    {
        string text = Text(row);

        if (text.StartsWith(Unchecked, StringComparison.Ordinal) || text.StartsWith(Checked, StringComparison.Ordinal))
        {
            return Look.Todo;
        }

        if (text.StartsWith(Dot, StringComparison.Ordinal))
        {
            return Look.Bullet;
        }

        return Counting(text) > 0 ? Look.Number : Look.Text;
    }

    private static int Counting(string text)
    {
        int dot = text.IndexOf('.');

        if (dot <= 0 || !text[..dot].All(char.IsAsciiDigit))
        {
            return 0;
        }

        return text[(dot + 1)..].StartsWith(Gap, StringComparison.Ordinal) ? dot + 1 + Gap.Length : 0;
    }

    public static bool IsDone(Paragraph row) => Text(row).StartsWith(Checked, StringComparison.Ordinal);

    public static int Counter(Paragraph row)
    {
        string text = Text(row);
        int dot = text.IndexOf('.');

        return dot > 0 && int.TryParse(text[..dot], out int number) ? number : 0;
    }

    public static int MarkLength(Paragraph row)
    {
        string text = Text(row);

        if (text.StartsWith(Unchecked, StringComparison.Ordinal) || text.StartsWith(Checked, StringComparison.Ordinal) || text.StartsWith(Dot, StringComparison.Ordinal))
        {
            return Dot.Length;
        }

        return Counting(text);
    }

    public static Thickness Indent(Look look) => look switch
    {
        Look.Quote => new Thickness(12, 0, 0, 0),
        Look.Bullet or Look.Number or Look.Todo => new Thickness(14, 0, 0, 0),
        Look.Code => new Thickness(12, 8, 12, 8),
        Look.Rule => new Thickness(0, 0, 0, 7),
        _ => default
    };

    private static string Text(Paragraph row) =>
        row.Inlines.FirstOrDefault() is Run first ? first.Text : LiveFormatter.TextOf(row);
}
