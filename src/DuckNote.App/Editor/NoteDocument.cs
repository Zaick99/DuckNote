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

    public static FlowDocument Empty(Brush foreground) => new()
    {
        PagePadding = new Thickness(0),
        FontFamily = new FontFamily(UiFonts),
        FontSize = 13.5,
        LineHeight = 21,
        PageWidth = double.NaN,
        Foreground = foreground
    };

    public static FlowDocument FromText(string text, MarkdownRenderer renderer, Brush foreground)
    {
        FlowDocument document = Empty(foreground);
        List<string> fenced = [];
        bool inCode = false;

        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (LineParser.IsFence(line))
            {
                if (inCode)
                {
                    document.Blocks.Add(renderer.CodeBlock(fenced));
                    fenced.Clear();
                }

                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                fenced.Add(line);
                continue;
            }

            Paragraph paragraph = new() { Margin = new Thickness(0) };
            document.Blocks.Add(paragraph);
            renderer.Render(paragraph, line);
        }

        if (fenced.Count > 0)
        {
            document.Blocks.Add(renderer.CodeBlock(fenced));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
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
