using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public static class NoteDocument
{
    public const string UiFonts = "SF Pro Text, Segoe UI Variable Text, Segoe UI";

    public static readonly Thickness Flush = new(0);

    public static Paragraph Row() => new() { Margin = Flush };

    /// <summary>
    /// Tutti i blocchi in ordine di lettura, scendendo dentro elenchi, tabelle e
    /// sezioni. E' l'unico cammino nel documento: chi cerca i paragrafi o i
    /// riquadri filtra questo.
    /// </summary>
    public static IEnumerable<Block> Deep(IEnumerable<Block>? blocks)
    {
        if (blocks is null)
        {
            yield break;
        }

        foreach (Block block in blocks)
        {
            yield return block;

            foreach (Block nested in Deep(Children(block)))
            {
                yield return nested;
            }
        }
    }

    public static IEnumerable<Block> Children(Block block) => block switch
    {
        List list => list.ListItems.SelectMany(item => item.Blocks),
        Table table => table.RowGroups
            .SelectMany(group => group.Rows)
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Blocks),
        Section section => section.Blocks,
        _ => []
    };

    /// <summary>
    /// Quanto spazio ha davvero una riga di testo nel controllo che mostra il
    /// documento. Non si indovina togliendo pixel alla finestra: la larghezza
    /// utile la sa il contenitore che fa scorrere il documento, bordi e barra
    /// di scorrimento gia' levati.
    /// </summary>
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

    public static BlockCollection? Siblings(Block block) => block.Parent switch
    {
        FlowDocument document => document.Blocks,
        TableCell cell => cell.Blocks,
        ListItem item => item.Blocks,
        Section section => section.Blocks,
        _ => null
    };

    public static bool Flatten(FlowDocument? document)
    {
        bool changed = false;

        foreach (Paragraph row in LiveFormatter.RowsOf(document))
        {
            // Una riga di separazione tiene i suoi margini: sono il suo
            // respiro, non spazio di troppo lasciato da una nota vecchia.
            if (row.Margin == Flush || Looks.Is(row, Look.Rule))
            {
                continue;
            }

            row.Margin = Flush;
            changed = true;
        }

        return changed;
    }

    public static FlowDocument Empty(Brush foreground) => new()
    {
        PagePadding = new Thickness(0),
        FontFamily = new FontFamily(UiFonts),
        FontSize = 13.5,
        LineHeight = 21,
        PageWidth = double.NaN,
        Foreground = foreground
    };

    public static FlowDocument FromText(string text, Brush foreground)
    {
        FlowDocument document = Empty(foreground);

        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            Paragraph paragraph = Row();

            if (line.Length > 0)
            {
                paragraph.Inlines.Add(new Run(line) { Foreground = foreground });
            }

            document.Blocks.Add(paragraph);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(Row());
        }

        return document;
    }

    public static byte[] ToXaml(RichTextBox editor) => ToXaml(editor.Document);

    public static byte[] ToXaml(FlowDocument document)
    {
        TextRange range = new(document.ContentStart, document.ContentEnd);
        using MemoryStream stream = new();
        range.Save(stream, DataFormats.Xaml);
        return stream.ToArray();
    }

    public static string ToPlainText(RichTextBox editor) =>
        new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;

    public static FlowDocument? FromXaml(byte[] bytes, Brush foreground)
    {
        try
        {
            FlowDocument document = Empty(foreground);
            using MemoryStream stream = new(bytes, writable: false);
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Xaml);
            return document;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or XamlParseException)
        {
            return null;
        }
    }
}
