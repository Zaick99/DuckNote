using System.Windows;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>Il tipo di una riga. Non e' scritto da nessuna parte: si legge da come la riga e' vestita.</summary>
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

/// <summary>
/// Come si riconosce il tipo di una riga. E' il formato delle note salvate: un titolo e' una
/// riga di 21/17/15 punti, un blocco di codice ha uno sfondo, una citazione un bordo a
/// sinistra, una separazione un bordo sotto; elenchi e spunte cominciano con il loro segno.
/// </summary>
public static class Looks
{
    public static readonly double[] HeadingSizes = [21, 17, 15];

    private const string Gap = " ";

    public const string Dot = "●" + Gap;
    public const string Unchecked = "☐" + Gap;
    public const string Checked = "☑" + Gap;

    public static readonly Thickness ListIndent = new(14, 0, 0, 0);
    public static readonly Thickness QuoteIndent = new(12, 0, 0, 0);
    public static readonly Thickness CodePadding = new(12, 8, 12, 8);

    public static string Counted(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Gap;

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

        for (int i = 0; i < HeadingSizes.Length; i++)
        {
            if (Math.Abs(row.FontSize - HeadingSizes[i]) < 0.6)
            {
                return Look.Heading1 + i;
            }
        }

        string text = Lead(row);

        if (text.StartsWith(Unchecked, StringComparison.Ordinal) || text.StartsWith(Checked, StringComparison.Ordinal))
        {
            return Look.Todo;
        }

        if (text.StartsWith(Dot, StringComparison.Ordinal))
        {
            return Look.Bullet;
        }

        return NumberMark(text) > 0 ? Look.Number : Look.Text;
    }

    public static bool Is(Paragraph? row, Look look) => Of(row) == look;

    public static bool IsListed(Look look) => look is Look.Bullet or Look.Number or Look.Todo;

    public static bool IsDone(Paragraph row) => Lead(row).StartsWith(Checked, StringComparison.Ordinal);

    public static int Counter(Paragraph row)
    {
        string text = Lead(row);
        int dot = text.IndexOf('.');

        return dot > 0 && int.TryParse(text[..dot], out int number) ? number : 0;
    }

    /// <summary>Quanti caratteri occupa il segno in testa alla riga (0 se non ce n'e').</summary>
    public static int MarkLength(Paragraph row)
    {
        string text = Lead(row);

        return text.StartsWith(Unchecked, StringComparison.Ordinal)
            || text.StartsWith(Checked, StringComparison.Ordinal)
            || text.StartsWith(Dot, StringComparison.Ordinal)
                ? Dot.Length
                : NumberMark(text);
    }

    /// <summary>Il punto cade sul segno della riga (per spuntare una casella con un clic).</summary>
    public static bool OnMark(Paragraph row, TextPointer point)
    {
        try
        {
            return new TextRange(row.ContentStart, point).Text.Length < MarkLength(row);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int NumberMark(string text)
    {
        int dot = text.IndexOf('.');

        if (dot <= 0 || !text[..dot].All(char.IsAsciiDigit))
        {
            return 0;
        }

        return text[(dot + 1)..].StartsWith(Gap, StringComparison.Ordinal) ? dot + 1 + Gap.Length : 0;
    }

    private static string Lead(Paragraph row) =>
        row.Inlines.FirstInline is Run first ? first.Text : Doc.TextOf(row);
}
