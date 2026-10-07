using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

public sealed partial class LiveFormatter(RichTextBox editor, MarkdownRenderer renderer, HostPaint hosts)
{
    private readonly HashSet<Paragraph> _dirty = [];

    public bool IsFormatting { get; private set; }

    public bool Suspended { get; set; }

    public InputRules? Rules { get; set; }

    public Paragraph? CaretParagraph => editor.CaretPosition?.Paragraph;

    public void MarkDirty(Paragraph? paragraph)
    {
        if (paragraph is not null)
        {
            _dirty.Add(paragraph);
        }
    }

    public void Flush()
    {
        if (Suspended)
        {
            _dirty.Clear();
            return;
        }

        Paragraph[] pending = [.. _dirty];
        _dirty.Clear();

        foreach (Paragraph row in pending)
        {
            Format(row);
        }
    }

    public void Format(Paragraph? row)
    {
        if (row is null || Suspended || row.Parent is null)
        {
            return;
        }

        IsFormatting = true;
        try
        {
            if (renderer.LiveFormatting)
            {
                Rules?.Apply(row);
            }

            hosts.Paint(row);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            IsFormatting = false;
        }
    }

    public void PaintAll()
    {
        IsFormatting = true;
        try
        {
            foreach (Paragraph row in Rows())
            {
                hosts.Paint(row);
            }
        }
        finally
        {
            IsFormatting = false;
        }
    }

    public void RecolourAll()
    {
        foreach (Paragraph row in Rows())
        {
            hosts.Recolour(row);
        }
    }

    public void Import()
    {
        Paragraph[] rows = Rows();

        IsFormatting = true;
        try
        {
            bool inCode = false;
            List<Paragraph> fenced = [];

            foreach (Paragraph row in rows)
            {
                string line = Line(row);

                if (LineParser.IsFence(line))
                {
                    inCode = !inCode;
                    continue;
                }

                if (inCode)
                {
                    fenced.Add(row);
                    continue;
                }

                Draw(row, line);
            }

            Fold(rows, fenced);
        }
        finally
        {
            IsFormatting = false;
        }

        PaintAll();
    }

    public bool NeedsImport() => Rows().Any(Unconverted);

    private static bool Unconverted(Paragraph row) =>
        Looks.Of(row) == Look.Text && Markers().IsMatch(Line(row));

    private void Fold(Paragraph[] rows, List<Paragraph> fenced)
    {
        CodeBlocks blocks = new(editor, renderer);

        foreach (Paragraph[] run in Runs(rows, fenced))
        {
            blocks.Fold(run);
        }

        foreach (Paragraph row in rows)
        {
            if (row.Parent is not null && LineParser.IsFence(Line(row)))
            {
                Remove(row);
            }
        }
    }

    private static List<Paragraph[]> Runs(Paragraph[] rows, List<Paragraph> fenced)
    {
        List<Paragraph[]> groups = [];
        List<Paragraph> current = [];

        foreach (Paragraph row in rows)
        {
            if (fenced.Contains(row) && (current.Count == 0 || ReferenceEquals(row.Parent, current[0].Parent)))
            {
                current.Add(row);
                continue;
            }

            if (current.Count > 0)
            {
                groups.Add([.. current]);
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            groups.Add([.. current]);
        }

        return groups;
    }

    private void Remove(Paragraph row)
    {
        if (row.Parent is FlowDocument document)
        {
            document.Blocks.Remove(row);
        }
    }

    private void Draw(Paragraph row, string line)
    {
        try
        {
            editor.BeginChange();
            try
            {
                renderer.Render(row, line);
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

    public Paragraph[] Rows() => RowsOf(editor.Document);

    public IEnumerable<Paragraph> AllParagraphs() => Rows();

    public static Paragraph[] RowsOf(FlowDocument? document) => [.. Walk(document?.Blocks)];

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

    private static string Line(Paragraph row) =>
        TextOf(row).Replace("\r", string.Empty).Replace("\n", string.Empty);

    [GeneratedRegex(@"\*\*[^\*]+\*\*|__[^_]+__|~~[^~]+~~|==[^=]+==|`[^`]+`|\[[^\]]*\]\([^)]*\)|^#{1,3}\s|^>\s|^(?:-{3,}|\*{3,}|_{3,})\s*$|^(?:```|~~~)|^[-*+]\s|^\d+[.)]\s")]
    private static partial Regex Markers();
}
