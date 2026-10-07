using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed class LiveFormatter(RichTextBox editor, MarkdownRenderer renderer)
{
    /// <summary>
    /// Quante righe si risale per sapere se si sta dentro un blocco di codice.
    /// Oltre, si assume di starne fuori: contare fino in cima a ogni battuta
    /// costerebbe piu' di quanto valga la risposta.
    /// </summary>
    private const int Lookback = 400;

    private readonly HashSet<Paragraph> _dirty = [];

    public bool IsFormatting { get; private set; }

    public bool Suspended { get; set; }

    public void MarkDirty(Paragraph? paragraph)
    {
        if (paragraph is not null)
        {
            _dirty.Add(paragraph);
        }
    }

    public Paragraph? CaretParagraph => editor.CaretPosition?.Paragraph;

    public void Flush()
    {
        if (Suspended || !renderer.LiveFormatting)
        {
            _dirty.Clear();
            return;
        }

        if (!editor.Selection.IsEmpty)
        {
            return;
        }

        Paragraph[] pending = [.. _dirty];
        _dirty.Clear();

        foreach (Paragraph paragraph in pending)
        {
            Format(paragraph);
        }
    }

    public void Format(Paragraph? paragraph)
    {
        if (paragraph is null || Suspended || !renderer.LiveFormatting || paragraph.Parent is null)
        {
            return;
        }

        if (!editor.Selection.IsEmpty)
        {
            return;
        }

        string text = TextOf(paragraph);

        if (paragraph.Tag is string drawn && drawn == text)
        {
            return;
        }

        int caret = CaretOffsetIn(paragraph);
        IsFormatting = true;
        try
        {
            Draw(paragraph, text, InsideCode(paragraph));

            if (caret >= 0)
            {
                RestoreCaret(paragraph, caret);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            IsFormatting = false;
        }
    }

    public void FormatAll()
    {
        if (editor.Document is null)
        {
            return;
        }

        // L'elenco si fissa prima di toccare niente: ridisegnare una riga
        // cambia il documento, e l'enumeratore delle righe non sopravvive al
        // primo cambiamento.
        Paragraph[] rows = [.. AllParagraphs()];

        IsFormatting = true;
        try
        {
            // Le righe passano in ordine: il recinto del codice si tiene a
            // mente invece di risalirlo ogni volta.
            bool inCode = false;

            foreach (Paragraph row in rows)
            {
                string text = TextOf(row);
                bool fence = LineParser.IsFence(text);

                Draw(row, text, inCode);

                if (fence)
                {
                    inCode = !inCode;
                }
            }
        }
        finally
        {
            IsFormatting = false;
        }
    }

    /// <summary>
    /// Ridisegna una riga. Una riga che si rifiuta non ferma le altre: senza
    /// questo, un solo pointer scaduto lasciava tutto il resto del documento
    /// senza formattazione.
    /// </summary>
    private void Draw(Paragraph row, string text, bool inCode)
    {
        try
        {
            editor.BeginChange();
            try
            {
                row.Inlines.Clear();
                renderer.Render(row, text, inCode);
                row.Tag = TextOf(row);
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public IEnumerable<Paragraph> AllParagraphs() => Walk(editor.Document?.Blocks);

    /// <summary>
    /// Se la riga sta dentro un recinto di codice: si risale contando i
    /// marcatori, e un numero dispari vuol dire che si e' dentro.
    /// </summary>
    private static bool InsideCode(Paragraph paragraph)
    {
        int fences = 0;
        int looked = 0;

        for (Block? above = paragraph.PreviousBlock; above is Paragraph line && looked < Lookback; above = above.PreviousBlock)
        {
            if (LineParser.IsFence(TextOf(line)))
            {
                fences++;
            }

            looked++;
        }

        return fences % 2 == 1;
    }

    private static IEnumerable<Paragraph> Walk(IEnumerable<Block>? blocks)
    {
        if (blocks is null)
        {
            yield break;
        }

        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;

                case List list:
                    foreach (ListItem item in list.ListItems)
                    {
                        foreach (Paragraph nested in Walk(item.Blocks))
                        {
                            yield return nested;
                        }
                    }
                    break;

                case Table table:
                    foreach (TableRowGroup group in table.RowGroups)
                    {
                        foreach (TableRow row in group.Rows)
                        {
                            foreach (TableCell cell in row.Cells)
                            {
                                foreach (Paragraph nested in Walk(cell.Blocks))
                                {
                                    yield return nested;
                                }
                            }
                        }
                    }
                    break;

                case Section section:
                    foreach (Paragraph nested in Walk(section.Blocks))
                    {
                        yield return nested;
                    }
                    break;

                default:
                    break;
            }
        }
    }

    public static string TextOf(Paragraph paragraph)
    {
        try
        {
            return new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private int CaretOffsetIn(Paragraph paragraph)
    {
        TextPointer? caret = editor.CaretPosition;
        if (caret is null || !ReferenceEquals(caret.Paragraph, paragraph))
        {
            return -1;
        }

        try
        {
            return new TextRange(paragraph.ContentStart, caret).Text.Length;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private void RestoreCaret(Paragraph paragraph, int offset)
    {
        TextPointer? position = paragraph.ContentStart;
        int remaining = offset;

        while (position is not null && remaining > 0 && position.CompareTo(paragraph.ContentEnd) < 0)
        {
            if (position.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                position = position.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int step = Math.Min(position.GetTextInRun(LogicalDirection.Forward).Length, remaining);
            position = position.GetPositionAtOffset(step);
            remaining -= step;
        }

        editor.CaretPosition = position ?? paragraph.ContentEnd;
    }
}
