using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed partial class EditorCommands(RichTextBox editor, LiveFormatter formatter)
{
    public event Action<string>? Refused;

    public void Wrap(string open, string? close = null)
    {
        close ??= open;
        TextSelection selection = editor.Selection;

        if (!selection.IsEmpty && !ReferenceEquals(selection.Start.Paragraph, selection.End.Paragraph))
        {
            Refused?.Invoke("Seleziona il testo dentro una sola riga.");
            return;
        }

        formatter.Suspended = true;
        try
        {
            if (selection.IsEmpty)
            {
                selection.Text = open + close;

                TextPointer? inside = editor.CaretPosition.GetPositionAtOffset(-close.Length);
                if (inside is not null)
                {
                    editor.CaretPosition = inside;
                }
            }
            else
            {
                string text = selection.Text;
                string trimmed = text.Trim();

                selection.Text =
                    trimmed.StartsWith(open, StringComparison.Ordinal)
                    && trimmed.EndsWith(close, StringComparison.Ordinal)
                    && trimmed.Length > open.Length + close.Length
                        ? trimmed.Substring(open.Length, trimmed.Length - open.Length - close.Length)
                        : open + text + close;
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

    public void SetLinePrefix(string prefix, bool toggle = true)
    {
        Paragraph? paragraph = formatter.CaretParagraph;
        if (paragraph is null)
        {
            return;
        }

        string text = LiveFormatter.TextOf(paragraph).Replace("\r", string.Empty).Replace("\n", string.Empty);
        string stripped = ExistingPrefix().Replace(text, string.Empty);
        string replacement = toggle && text == prefix + stripped ? stripped : prefix + stripped;

        formatter.Suspended = true;
        try
        {
            new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text = replacement;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            formatter.Suspended = false;
        }

        paragraph.Tag = null;
        formatter.Format(paragraph);
    }

    /// <summary>
    /// Recinta il testo fra due righe di marcatori, su righe loro: un blocco di
    /// codice non si apre e si chiude a meta' riga.
    /// </summary>
    public void WrapBlock(string fence)
    {
        Paragraph? here = formatter.CaretParagraph;
        if (here is null)
        {
            return;
        }

        formatter.Suspended = true;
        try
        {
            string inside = LiveFormatter.TextOf(here).Replace("\r", string.Empty).Replace("\n", string.Empty);

            new TextRange(here.ContentStart, here.ContentEnd).Text = fence;

            Paragraph body = new(new Run(inside));
            Paragraph close = new(new Run(fence));

            editor.Document.Blocks.InsertAfter(here, body);
            editor.Document.Blocks.InsertAfter(body, close);
            editor.CaretPosition = body.ContentEnd;
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
    }

    /// <summary>Una riga tutta sua, sotto quella corrente.</summary>
    public void InsertLine(string text)
    {
        Paragraph? here = formatter.CaretParagraph;
        if (here is null)
        {
            return;
        }

        formatter.Suspended = true;
        try
        {
            if (LiveFormatter.TextOf(here).Trim().Length == 0)
            {
                new TextRange(here.ContentStart, here.ContentEnd).Text = text;
                editor.CaretPosition = here.ContentEnd;
            }
            else
            {
                Paragraph added = new(new Run(text));
                editor.Document.Blocks.InsertAfter(here, added);
                editor.CaretPosition = added.ContentEnd;
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
    }

    public void ClearFormatting()
    {
        TextSelection selection = editor.Selection;
        if (selection.IsEmpty)
        {
            Refused?.Invoke("Seleziona il testo da ripulire.");
            return;
        }

        if (!ReferenceEquals(selection.Start.Paragraph, selection.End.Paragraph))
        {
            Refused?.Invoke("Seleziona il testo dentro una sola riga.");
            return;
        }

        formatter.Suspended = true;
        try
        {
            string text = selection.Text;
            text = BoldMarks().Replace(text, "$1");
            text = UnderMarks().Replace(text, "$1");
            text = StrikeMarks().Replace(text, "$1");
            text = ItalicMarks().Replace(text, "$1");
            text = CodeMarks().Replace(text, "$1");
            text = LinePrefixes().Replace(text, string.Empty);
            selection.Text = text;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            formatter.Suspended = false;
        }

        Redraw();
    }

    private void Redraw()
    {
        Paragraph? paragraph = formatter.CaretParagraph;
        if (paragraph is null)
        {
            return;
        }

        paragraph.Tag = null;
        formatter.Format(paragraph);
    }

    [GeneratedRegex(@"^(#{1,3}\s+|>\s?)")]
    private static partial Regex ExistingPrefix();

    [GeneratedRegex(@"\*\*([^\*]+)\*\*")]
    private static partial Regex BoldMarks();

    [GeneratedRegex("__([^_]+)__")]
    private static partial Regex UnderMarks();

    [GeneratedRegex("~~([^~]+)~~")]
    private static partial Regex StrikeMarks();

    [GeneratedRegex(@"\*([^\*]+)\*")]
    private static partial Regex ItalicMarks();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeMarks();

    [GeneratedRegex(@"(?m)^(#{1,3}\s+|>\s?)")]
    private static partial Regex LinePrefixes();
}
