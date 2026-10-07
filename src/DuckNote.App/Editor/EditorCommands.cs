using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// I comandi della barra di formattazione. Lavorano sulle righe toccate dalla
/// selezione: se sono tre, cambiano tutte e tre, qualunque sia il pulsante.
/// </summary>
public sealed partial class EditorCommands(RichTextBox editor, LiveFormatter formatter)
{
    public event Action<string>? Refused;

    // --- marcatori attorno al testo ----------------------------------------

    public void Wrap(string open, string? close = null)
    {
        close ??= open;
        TextSelection selection = editor.Selection;

        if (selection.IsEmpty)
        {
            Open(open, close);
            return;
        }

        if (ReferenceEquals(selection.Start.Paragraph, selection.End.Paragraph))
        {
            Dress(selection, open, close);
            return;
        }

        Lines(Rows(), open, close);
    }

    /// <summary>Senza selezione i marcatori nascono vuoti, col cursore in mezzo.</summary>
    private void Open(string open, string close)
    {
        formatter.Suspended = true;
        try
        {
            editor.Selection.Text = open + close;

            if (editor.CaretPosition.GetPositionAtOffset(-close.Length) is { } inside)
            {
                editor.CaretPosition = inside;
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            formatter.Suspended = false;
        }

        Redraw();
    }

    /// <summary>Dentro una riga sola si veste il pezzo selezionato, non la riga.</summary>
    private void Dress(TextSelection selection, string open, string close)
    {
        formatter.Suspended = true;
        try
        {
            string text = selection.Text;
            selection.Text = Wrapped(text.Trim(), open, close) ? Undressed(text.Trim(), open, close) : open + text + close;
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            formatter.Suspended = false;
        }

        Redraw();
    }

    /// <summary>
    /// Su piu' righe ogni riga si veste da sola: un marcatore Markdown non
    /// attraversa un fine riga, e un grassetto aperto su una riga e chiuso su
    /// un'altra non sarebbe grassetto. Il prefisso resta fuori dai marcatori,
    /// altrimenti smetterebbe di essere un prefisso.
    /// </summary>
    private void Lines(List<Paragraph> rows, string open, string close)
    {
        string[] bodies = [.. rows.Select(row => Split(row).Body).Where(body => body.Length > 0)];

        if (bodies.Length == 0)
        {
            Refused?.Invoke("Le righe selezionate sono vuote.");
            return;
        }

        bool undress = bodies.All(body => Wrapped(body, open, close));

        Change(rows, row =>
        {
            (string prefix, string body) = Split(row);

            return body.Length == 0 ? null : prefix + (undress ? Undressed(body, open, close) : open + body + close);
        });
    }

    private static bool Wrapped(string text, string open, string close) =>
        text.Length > open.Length + close.Length
        && text.StartsWith(open, StringComparison.Ordinal)
        && text.EndsWith(close, StringComparison.Ordinal);

    private static string Undressed(string text, string open, string close) =>
        text.Substring(open.Length, text.Length - open.Length - close.Length);

    // --- prefissi di riga ---------------------------------------------------

    /// <summary>
    /// Titoli, citazioni, elenchi e spunte. Il prefisso che c'e' viene tolto
    /// prima di metterne un altro: un elenco premuto su una riga numerata deve
    /// dare un elenco, non `- 1. testo`.
    /// </summary>
    public void SetLinePrefix(string prefix, bool toggle = true)
    {
        List<Paragraph> rows = Rows();
        if (rows.Count == 0)
        {
            return;
        }

        bool counted = Numbered().IsMatch(prefix);
        bool single = rows.Count == 1;
        bool strip = toggle && rows.All(row => Wears(row, prefix, counted));

        int number = 0;

        Change(rows, row =>
        {
            string body = Split(row).Body;

            // Una riga vuota in mezzo a una selezione resta vuota: un elenco
            // non ha una voce fatta di niente.
            if (body.Length == 0 && !single)
            {
                return null;
            }

            if (strip)
            {
                return body;
            }

            number++;

            return (counted ? $"{number}. " : prefix) + body;
        });
    }

    private static bool Wears(Paragraph row, string prefix, bool counted)
    {
        string worn = Split(row).Prefix;

        return counted ? Numbered().IsMatch(worn) : worn == prefix;
    }

    // --- recinti e righe a se' ---------------------------------------------

    /// <summary>
    /// Recinta le righe selezionate fra due marcatori, su righe loro: un blocco
    /// di codice non si apre a meta' riga, e quello che si e' selezionato e'
    /// esattamente quello che va dentro.
    /// </summary>
    public void WrapBlock(string fence)
    {
        List<Paragraph> rows = Rows();
        if (rows.Count == 0)
        {
            return;
        }

        if (Siblings(rows[0]) is not { } where || Siblings(rows[^1]) is null)
        {
            Refused?.Invoke("Un blocco di codice non entra qui.");
            return;
        }

        Paragraph body = rows[^1];

        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                where.InsertBefore(rows[0], new Paragraph(new Run(fence)));
                where.InsertAfter(body, new Paragraph(new Run(fence)));
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            formatter.Suspended = false;
        }

        formatter.FormatAll();
        editor.CaretPosition = body.ContentEnd;
    }

    /// <summary>Una riga tutta sua, sotto l'ultima selezionata.</summary>
    public void InsertLine(string text)
    {
        List<Paragraph> rows = Rows();
        if (rows.Count == 0)
        {
            return;
        }

        Paragraph last = rows[^1];
        Paragraph landing = last;

        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                if (rows.Count == 1 && LiveFormatter.TextOf(last).Trim().Length == 0)
                {
                    new TextRange(last.ContentStart, last.ContentEnd).Text = text;
                }
                else if (Siblings(last) is { } where)
                {
                    landing = new Paragraph(new Run(text));
                    where.InsertAfter(last, landing);
                }
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            formatter.Suspended = false;
        }

        formatter.FormatAll();
        editor.CaretPosition = landing.ContentEnd;
    }

    // --- ripulire ----------------------------------------------------------

    public void ClearFormatting()
    {
        TextSelection selection = editor.Selection;

        if (!selection.IsEmpty && ReferenceEquals(selection.Start.Paragraph, selection.End.Paragraph))
        {
            formatter.Suspended = true;
            try
            {
                selection.Text = Bare(selection.Text);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                formatter.Suspended = false;
            }

            Redraw();
            return;
        }

        List<Paragraph> rows = Rows();
        if (rows.Count == 0)
        {
            return;
        }

        Change(rows, row => Bare(LiveFormatter.TextOf(row).Replace("\r", string.Empty).Replace("\n", string.Empty)));
    }

    /// <summary>Il testo senza i suoi marcatori, prefisso di riga compreso.</summary>
    private static string Bare(string text)
    {
        text = BoldMarks().Replace(text, "$1");
        text = UnderMarks().Replace(text, "$1");
        text = StrikeMarks().Replace(text, "$1");
        text = MarkMarks().Replace(text, "$1");
        text = ItalicMarks().Replace(text, "$1");
        text = CodeMarks().Replace(text, "$1");
        text = LinkMarks().Replace(text, "$1");

        return LinePrefixes().Replace(text, string.Empty);
    }

    // --- le righe su cui si lavora -----------------------------------------

    /// <summary>
    /// Le righe toccate dalla selezione, o quella del cursore se selezione non
    /// c'e'. Una selezione che finisce dove comincia la riga dopo non la
    /// comprende: chi trascina fino a capo riga non intendeva prenderla.
    /// </summary>
    private List<Paragraph> Rows()
    {
        TextSelection selection = editor.Selection;

        if (selection.IsEmpty)
        {
            return formatter.CaretParagraph is { } here ? [here] : [];
        }

        if (selection.Start.Paragraph is not { } first || selection.End.Paragraph is not { } last)
        {
            return [];
        }

        List<Paragraph> all = [.. formatter.AllParagraphs()];
        int from = all.IndexOf(first);
        int to = all.IndexOf(last);

        // Dell'ultima riga non e' selezionato niente: chi ha trascinato fino a
        // capo riga non intendeva prendere anche quella.
        if (to > from && Selected(last, selection).Length == 0)
        {
            to--;
        }

        return from < 0 || to < from ? [] : all[from..(to + 1)];
    }

    /// <summary>Quanto della riga cade dentro la selezione.</summary>
    private static string Selected(Paragraph row, TextSelection selection)
    {
        if (selection.End.CompareTo(row.ContentStart) <= 0)
        {
            return string.Empty;
        }

        try
        {
            return new TextRange(row.ContentStart, selection.End).Text.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Riscrive le righe in un colpo solo e rimette la selezione dove era: chi
    /// ha selezionato tre righe e premuto grassetto vuole poterci premere
    /// ancora.
    /// </summary>
    private void Change(List<Paragraph> rows, Func<Paragraph, string?> replacement)
    {
        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                foreach (Paragraph row in rows)
                {
                    if (replacement(row) is { } text)
                    {
                        new TextRange(row.ContentStart, row.ContentEnd).Text = text;
                    }
                }
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            formatter.Suspended = false;
        }

        foreach (Paragraph row in rows)
        {
            row.Tag = null;
        }

        formatter.FormatAll();

        if (rows.Count == 1)
        {
            editor.CaretPosition = rows[0].ContentEnd;
            return;
        }

        editor.Selection.Select(rows[0].ContentStart, rows[^1].ContentEnd);
    }

    /// <summary>Il prefisso di riga e quello che viene dopo.</summary>
    private static (string Prefix, string Body) Split(Paragraph row)
    {
        string text = LiveFormatter.TextOf(row).Replace("\r", string.Empty).Replace("\n", string.Empty);
        Match parts = Prefixed().Match(text);

        return (parts.Groups[1].Value, parts.Groups[2].Value);
    }

    /// <summary>La collezione a cui la riga appartiene: documento, cella, voce d'elenco.</summary>
    private static BlockCollection? Siblings(Paragraph row) => row.Parent switch
    {
        FlowDocument document => document.Blocks,
        TableCell cell => cell.Blocks,
        ListItem item => item.Blocks,
        Section section => section.Blocks,
        _ => null
    };

    private void Redraw()
    {
        if (formatter.CaretParagraph is not { } paragraph)
        {
            return;
        }

        paragraph.Tag = null;
        formatter.Format(paragraph);
    }

    [GeneratedRegex(@"^(\s*(?:#{1,6}[ \t]+|>+[ \t]?|[-*+][ \t]+\[[ xX]\][ \t]?|[-*+][ \t]+|\d+[.)][ \t]+))?(.*)$")]
    private static partial Regex Prefixed();

    [GeneratedRegex(@"^\d+[.)]\s+$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"\*\*([^\*]+)\*\*")]
    private static partial Regex BoldMarks();

    [GeneratedRegex("__([^_]+)__")]
    private static partial Regex UnderMarks();

    [GeneratedRegex("~~([^~]+)~~")]
    private static partial Regex StrikeMarks();

    [GeneratedRegex("==([^=]+)==")]
    private static partial Regex MarkMarks();

    [GeneratedRegex(@"\*([^\*]+)\*")]
    private static partial Regex ItalicMarks();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeMarks();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkMarks();

    [GeneratedRegex(@"(?m)^(\s*(?:#{1,6}[ \t]+|>+[ \t]?|[-*+][ \t]+\[[ xX]\][ \t]?|[-*+][ \t]+|\d+[.)][ \t]+))")]
    private static partial Regex LinePrefixes();
}
