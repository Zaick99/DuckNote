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

public sealed class EditorCommands(RichTextBox editor, LiveFormatter formatter, MarkdownRenderer renderer, CodeBlocks blocks)
{
    public event Action<string>? Refused;

    public void Toggle(Mark mark)
    {
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

            case Mark.Link:
                Address();
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
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(MarkdownRenderer.MonoFonts));
    }

    private void Address()
    {
        string selected = editor.Selection.Text.Trim();

        if (!selected.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !selected.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !selected.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            Refused?.Invoke("Seleziona un indirizzo, oppure scrivi [testo](indirizzo).");
            return;
        }

        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, renderer.Palette.Link);
        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
    }

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
                Paragraph rule = new();
                renderer.Dress(rule, Look.Rule);
                where.InsertAfter(last, rule);

                Paragraph after = new();
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

        Paragraph next = new();
        where.InsertAfter(row, next);

        if (listed)
        {
            renderer.Dress(next, look, Looks.Counter(row) + 1);
        }

        editor.CaretPosition = next.ContentEnd;

        return true;
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
            return;
        }

        editor.Selection.Select(rows[0].ContentStart, rows[^1].ContentEnd);
    }

    private static BlockCollection? Siblings(Paragraph row) => row.Parent switch
    {
        FlowDocument document => document.Blocks,
        TableCell cell => cell.Blocks,
        ListItem item => item.Blocks,
        Section section => section.Blocks,
        _ => null
    };
}
