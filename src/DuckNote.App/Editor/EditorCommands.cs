using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public enum Mark
{
    Bold,
    Italic,
    Underline,
    Strike,
    Highlight,
    Code,
    Link
}

public sealed record LinkChoice(string Address, string Text, bool Removed);

public sealed class EditorCommands(RichTextBox editor, LiveFormatter formatter, Dresser renderer, CodeBlocks blocks)
{
    public event Action<string>? Refused;

    public Func<string, string, LinkChoice?>? AskForLink { get; set; }

    public void Toggle(Mark mark)
    {
        if (mark == Mark.Link)
        {
            Address();
            return;
        }

        if (editor.Selection.IsEmpty)
        {
            Refused?.Invoke("Seleziona il testo da formattare.");
            return;
        }

        switch (mark)
        {
            case Mark.Bold:
                Flip(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
                break;

            case Mark.Italic:
                Flip(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal);
                break;

            case Mark.Underline:
                Decorate(TextDecorations.Underline[0]);
                break;

            case Mark.Strike:
                Decorate(TextDecorations.Strikethrough[0]);
                break;

            case Mark.Highlight:
                Paint(renderer.Palette.MarkBackground, renderer.Palette.MarkForeground);
                break;

            case Mark.Code:
                Letters();
                break;
        }
    }

    private void Flip(DependencyProperty property, object on, object off)
    {
        object current = editor.Selection.GetPropertyValue(property);

        editor.Selection.ApplyPropertyValue(property, Equals(current, on) ? off : on);
    }

    private void Decorate(TextDecoration wanted)
    {
        TextDecorationCollection now = editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection ?? [];
        bool already = now.Any(decoration => decoration.Location == wanted.Location);

        TextDecorationCollection next = new(now.Where(decoration => decoration.Location != wanted.Location));
        if (!already)
        {
            next.Add(wanted);
        }

        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
    }

    private void Paint(Brush background, Brush foreground)
    {
        bool already = ReferenceEquals(editor.Selection.GetPropertyValue(TextElement.BackgroundProperty), background);

        editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, already ? null : background);
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, already ? renderer.Palette.Text : foreground);
    }

    private void Letters()
    {
        bool already = ReferenceEquals(editor.Selection.GetPropertyValue(TextElement.BackgroundProperty), renderer.Palette.CodeBackground);

        if (already)
        {
            editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
            editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, renderer.Palette.Text);
            editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(NoteDocument.UiFonts));
            return;
        }

        editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, renderer.Palette.CodeBackground);
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, renderer.Palette.CodeForeground);
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(Dresser.MonoFonts));
    }

    private void Address()
    {
        if (AskForLink is null)
        {
            return;
        }

        Hyperlink? here = editor.Selection.IsEmpty
            ? LinkAt(editor.Selection.Start)
            : Covering(editor.Selection);

        if (here is null && editor.Selection.Start.Paragraph != editor.Selection.End.Paragraph)
        {
            Refused?.Invoke("Un collegamento sta su una riga sola: restringi la selezione.");
            return;
        }

        string shown = here is null
            ? editor.Selection.Text
            : new TextRange(here.ContentStart, here.ContentEnd).Text;

        if (AskForLink(shown, here?.NavigateUri?.ToString() ?? string.Empty) is not { } choice)
        {
            return;
        }

        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                Relink(here, choice);
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (Exception problem) when (problem is ArgumentException or InvalidOperationException)
        {
            Refused?.Invoke("Il collegamento non si e' potuto applicare a questa selezione.");
        }
        finally
        {
            formatter.Suspended = false;
        }
    }

    private void Relink(Hyperlink? here, LinkChoice choice)
    {
        if (choice.Removed)
        {
            Unlink(here);
            return;
        }

        if (here is not null)
        {
            here.NavigateUri = new Uri(choice.Address);
            here.ToolTip = choice.Address;
            return;
        }

        if (editor.Selection.IsEmpty)
        {
            Wrote(choice);
            return;
        }

        Dressed(new Hyperlink(editor.Selection.Start, editor.Selection.End), choice);
    }

    /// <summary>
    /// Niente di selezionato: il testo da mostrare arriva dalla finestra e
    /// viene scritto dove sta il cursore, poi vestito da collegamento.
    /// </summary>
    private void Wrote(LinkChoice choice)
    {
        if (choice.Text.Length == 0
            || editor.CaretPosition is not { Paragraph: { } row } caret)
        {
            return;
        }

        // Il pezzo di testo si costruisce e si mette dov'e' il cursore: contare
        // i caratteri per ritrovarne la fine sbaglia di uno, perche' entrare in
        // un Run conta come un passo.
        Run word = new(choice.Text);

        if (caret.GetAdjacentElement(LogicalDirection.Backward) is Inline before)
        {
            row.Inlines.InsertAfter(before, word);
        }
        else if (row.Inlines.FirstInline is { } first)
        {
            row.Inlines.InsertBefore(first, word);
        }
        else
        {
            row.Inlines.Add(word);
        }

        Dressed(new Hyperlink(word.ContentStart, word.ContentEnd), choice);
    }

    private void Dressed(Hyperlink fresh, LinkChoice choice)
    {
        fresh.NavigateUri = new Uri(choice.Address);
        fresh.ToolTip = choice.Address;
        fresh.Foreground = renderer.Palette.Link;

        editor.Selection.Select(fresh.ContentStart, fresh.ContentEnd);
    }

    private static void Unlink(Hyperlink? link)
    {
        if (link is null || Holder(link) is not { } around)
        {
            return;
        }

        foreach (Inline kept in (Inline[])[.. link.Inlines])
        {
            link.Inlines.Remove(kept);
            around.InsertBefore(link, kept);
        }

        around.Remove(link);
    }

    private static InlineCollection? Holder(Inline? inline) => inline?.Parent switch
    {
        Paragraph paragraph => paragraph.Inlines,
        Span span => span.Inlines,
        _ => null
    };

    public static Hyperlink? LinkAt(TextPointer? point)
    {
        if (point is null)
        {
            return null;
        }

        for (DependencyObject? up = point.Parent; up is not null; up = LogicalTreeHelper.GetParent(up))
        {
            if (up is Hyperlink inside)
            {
                return inside;
            }
        }

        return point.Paragraph is { } row ? Around(row, point) : null;
    }

    private static Hyperlink? Around(Paragraph row, TextPointer point) =>
        row.Inlines.OfType<Hyperlink>().FirstOrDefault(link =>
            link.ElementStart.CompareTo(point) <= 0 && link.ElementEnd.CompareTo(point) >= 0);

    private static Hyperlink? Covering(TextSelection selection) =>
        LinkAt(selection.Start) is { } link
            && link.ElementStart.CompareTo(selection.Start) <= 0
            && link.ElementEnd.CompareTo(selection.End) >= 0
                ? link
                : null;

    public void SetLook(Look look)
    {
        Paragraph[] rows = Rows();
        if (rows.Length == 0)
        {
            return;
        }

        bool strip = rows.All(row => Looks.Is(row, look));
        bool single = rows.Length == 1;
        int number = 0;

        Change(rows, row =>
        {
            if (strip)
            {
                renderer.Undress(row);
                return;
            }

            if (!single && LiveFormatter.TextOf(row).Trim().Length == 0)
            {
                return;
            }

            number++;
            renderer.Dress(row, look, number);
        });
    }

    public void ToggleCode()
    {
        Paragraph[] rows = Rows();
        if (rows.Length == 0)
        {
            return;
        }

        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                if (rows.Length == 1 && Looks.Is(rows[0], Look.Code))
                {
                    if (blocks.Unfold(rows[0]) is { } first)
                    {
                        editor.CaretPosition = first.ContentStart;
                    }

                    return;
                }

                if (blocks.Fold(rows) is { } block)
                {
                    editor.CaretPosition = block.ContentEnd;
                }
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            formatter.Suspended = false;
        }
    }

    public void InsertRule()
    {
        Paragraph[] rows = Rows();
        if (rows.Length == 0 || Siblings(rows[^1]) is not { } where)
        {
            return;
        }

        Paragraph last = rows[^1];

        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                // La separazione va sulla riga subito sotto a quella che si sta
                // scrivendo, e si continua a capo: una riga nuova, non l'inizio
                // di quel che c'era gia' li' sotto.
                Paragraph rule = NoteDocument.Row();
                renderer.Dress(rule, Look.Rule);
                where.InsertAfter(last, rule);

                Paragraph after = NoteDocument.Row();
                where.InsertAfter(rule, after);

                editor.CaretPosition = after.ContentStart;
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            formatter.Suspended = false;
        }
    }

    public bool NewLine()
    {
        if (formatter.CaretParagraph is not { } row || Siblings(row) is not { } where)
        {
            return false;
        }

        if (editor.CaretPosition is not { } caret || Rest(caret, row).Length > 0)
        {
            return false;
        }

        Look look = Looks.Of(row);
        if (look == Look.Text)
        {
            return false;
        }

        bool listed = look is Look.Bullet or Look.Number or Look.Todo;

        if (listed && LiveFormatter.TextOf(row).TrimEnd('\r', '\n').Length <= Looks.MarkLength(row))
        {
            renderer.Undress(row);
            return true;
        }

        Paragraph next = NoteDocument.Row();
        where.InsertAfter(row, next);

        if (listed)
        {
            renderer.Dress(next, look, Looks.Counter(row) + 1);
        }

        editor.CaretPosition = next.ContentEnd;
        WritesPlain(next);

        return true;
    }

    /// <summary>
    /// Il cursore lasciato subito dopo un marcatore eredita il suo pennello, e
    /// quel grigio in mono e' del segno dell'elenco, non di quello che la
    /// persona sta per scrivere. Su una selezione vuota ApplyPropertyValue
    /// scrive proprio quel che il prossimo carattere userà.
    /// </summary>
    private void WritesPlain(Paragraph row)
    {
        if (Looks.MarkLength(row) == 0 || !editor.Selection.IsEmpty)
        {
            return;
        }

        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, renderer.Palette.Text);
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(NoteDocument.UiFonts));
        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
    }

    public void ClearFormatting()
    {
        Paragraph[] rows = Rows();
        if (rows.Length == 0)
        {
            return;
        }

        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                if (!editor.Selection.IsEmpty)
                {
                    editor.Selection.ClearAllProperties();
                }

                foreach (Paragraph row in rows)
                {
                    renderer.Undress(row);
                }
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            formatter.Suspended = false;
        }

        formatter.PaintAll();
    }

    private Paragraph[] Rows()
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

        Paragraph[] all = formatter.Rows();
        int from = Array.IndexOf(all, first);
        int to = Array.IndexOf(all, last);

        if (to > from && Selected(last, selection).Length == 0)
        {
            to--;
        }

        return from < 0 || to < from ? [] : all[from..(to + 1)];
    }

    private static string Rest(TextPointer caret, Paragraph row)
    {
        try
        {
            return new TextRange(caret, row.ContentEnd).Text.Trim('\r', '\n');
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

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

    private void Change(Paragraph[] rows, Action<Paragraph> change)
    {
        formatter.Suspended = true;
        try
        {
            editor.BeginChange();
            try
            {
                foreach (Paragraph row in rows)
                {
                    change(row);
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

        if (rows.Length == 1)
        {
            editor.CaretPosition = rows[0].ContentEnd;
            WritesPlain(rows[0]);
            return;
        }

        editor.Selection.Select(rows[0].ContentStart, rows[^1].ContentEnd);
    }

    private static BlockCollection? Siblings(Paragraph row) => NoteDocument.Siblings(row);
}
