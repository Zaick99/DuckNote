using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace DuckNote.App.Editor;

/// <summary>Attrezzi sul FlowDocument della nota: crearlo, attraversarlo, salvarlo, misurarlo.</summary>
public static class Doc
{
    public const string TextFonts = "SF Pro Text, Segoe UI Variable Text, Segoe UI";
    public const string MonoFonts = "SF Mono, Cascadia Mono, Consolas, Menlo, Courier New";
    public const double BodySize = 13.5;

    public static readonly Thickness Flush = new(0);

    public static FlowDocument Empty()
    {
        FlowDocument document = new()
        {
            PagePadding = Flush,
            FontFamily = new FontFamily(TextFonts),
            FontSize = BodySize,
            LineHeight = 21,
            PageWidth = double.NaN
        };

        document.SetResourceReference(FlowDocument.ForegroundProperty, "Label");

        return document;
    }

    /// <summary>Una riga senza il margine automatico, che sarebbe un buco fra le righe.</summary>
    public static Paragraph Row(string text = "")
    {
        Paragraph row = new() { Margin = Flush };

        if (text.Length > 0)
        {
            row.Inlines.Add(new Run(text));
        }

        return row;
    }

    /// <summary>Tutti i blocchi in ordine di lettura, dentro elenchi, tabelle e sezioni.</summary>
    public static IEnumerable<Block> Deep(IEnumerable<Block>? blocks)
    {
        foreach (Block block in blocks ?? [])
        {
            yield return block;

            IEnumerable<Block> inner = block switch
            {
                List list => list.ListItems.SelectMany(item => item.Blocks),
                Table table => table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Blocks),
                Section section => section.Blocks,
                _ => []
            };

            foreach (Block nested in Deep(inner))
            {
                yield return nested;
            }
        }
    }

    public static Paragraph[] Rows(FlowDocument? document) => [.. Deep(document?.Blocks).OfType<Paragraph>()];

    public static BlockCollection? Siblings(Block block) => block.Parent switch
    {
        FlowDocument document => document.Blocks,
        TableCell cell => cell.Blocks,
        ListItem item => item.Blocks,
        Section section => section.Blocks,
        _ => null
    };

    public static string TextOf(TextElement element)
    {
        try
        {
            return new TextRange(element.ContentStart, element.ContentEnd).Text;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    /// <summary>Il testo di una riga senza gli a capo che TextRange aggiunge in fondo.</summary>
    public static string LineOf(Paragraph row) => TextOf(row).TrimEnd('\r', '\n');

    public static string PlainText(FlowDocument document) =>
        string.Join('\n', Rows(document).Select(row => LineOf(row)));

    /// <summary>Quanti caratteri di testo ci sono fra l'inizio della riga e il punto.</summary>
    public static int OffsetOf(Paragraph? row, TextPointer? point)
    {
        if (row is null || point is null || !ReferenceEquals(point.Paragraph, row))
        {
            return -1;
        }

        int seen = 0;

        for (TextPointer? at = row.ContentStart; at is not null && at.CompareTo(row.ContentEnd) < 0;)
        {
            if (at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
            {
                at = at.GetNextContextPosition(LogicalDirection.Forward);
                continue;
            }

            int length = at.GetTextRunLength(LogicalDirection.Forward);
            TextPointer? next = at.GetPositionAtOffset(length);

            if (next is null || next.CompareTo(point) > 0)
            {
                return seen + Math.Max(0, at.GetOffsetToPosition(point));
            }

            seen += length;
            at = next;
        }

        return seen;
    }

    /// <summary>Il punto della riga dopo tanti caratteri di testo (il contrario di <see cref="OffsetOf"/>).</summary>
    public static TextPointer? PointAt(Paragraph row, int offset)
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

            int step = Math.Min(at.GetTextRunLength(LogicalDirection.Forward), left);
            at = at.GetPositionAtOffset(step);
            left -= step;
        }

        return left == 0 ? at : null;
    }

    public static byte[] ToXaml(FlowDocument document)
    {
        TextRange range = new(document.ContentStart, document.ContentEnd);
        using MemoryStream stream = new();
        range.Save(stream, DataFormats.Xaml);

        return stream.ToArray();
    }

    public static FlowDocument? FromXaml(byte[] bytes)
    {
        try
        {
            FlowDocument document = Empty();
            using MemoryStream stream = new(bytes, writable: false);
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Xaml);

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(Row());
            }

            return document;
        }
        catch (Exception problem) when (problem is ArgumentException or InvalidOperationException or XamlParseException)
        {
            return null;
        }
    }

    /// <summary>La larghezza utile di una riga nel controllo che mostra il documento.</summary>
    public static double? Column(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ScrollViewer { Name: "PART_ContentHost", ViewportWidth: > 0 } host)
        {
            return host.ViewportWidth;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (Column(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public static ScrollViewer? Scroller(RichTextBox editor) =>
        editor.Template?.FindName("PART_ContentHost", editor) as ScrollViewer;
}
