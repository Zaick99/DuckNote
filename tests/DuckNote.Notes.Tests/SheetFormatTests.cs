using System.Text;
using System.Text.Json.Nodes;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Sheets;

namespace DuckNote.Notes.Tests;

/// <summary>
/// Il foglio fra il file della nota (lo XAML della versione WPF) e l'editor: si legge come lo
/// legge la versione WPF, si riscrive vestito allo stesso modo, e niente si perde andando e
/// tornando.
/// </summary>
public class SheetFormatTests
{
    private const string Mono = "SF Mono, Cascadia Mono, Consolas, Menlo, Courier New";

    /// <summary>Un foglio come lo salva TextRange.Save della versione WPF (colori del tema chiaro).</summary>
    private static readonly string Wpf =
        "<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\" TextAlignment=\"Left\" " +
        "LineHeight=\"21\" FontFamily=\"SF Pro Text, Segoe UI Variable Text, Segoe UI\" FontSize=\"13.5\" Foreground=\"#FF1F2328\">" +
        "<Paragraph Margin=\"0,0,0,0\" FontSize=\"21\" FontWeight=\"SemiBold\"><Run>DuckNote</Run></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\"><Run>Il gateway </Run><Run FontFamily=\"" + Mono + "\" Foreground=\"#FF1F9D45\">192.168.1.1</Run>" +
        "<Run> e </Run><Run FontWeight=\"Bold\">grassetto</Run><Run> </Run><Run Foreground=\"#FFC0392B\">rosso</Run><Run> </Run>" +
        "<Run Background=\"#FFFFEBC2\" Foreground=\"#FF1F2328\">evidenziato</Run><Run> </Run>" +
        "<Run Background=\"#FFF1F1F3\" Foreground=\"#FFB4009E\" FontFamily=\"" + Mono + "\">code</Run><Run> </Run>" +
        "<Hyperlink NavigateUri=\"https://example.com/\" Foreground=\"#FF2E6BE6\"><Run>sito</Run></Hyperlink></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\" Padding=\"14,0,0,0\" TextIndent=\"-14\"><Run FontFamily=\"" + Mono + "\" Foreground=\"#FF8D949C\">● </Run><Run>voce</Run></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\" Padding=\"14,0,0,0\" TextIndent=\"-14\"><Run FontFamily=\"" + Mono + "\" Foreground=\"#FF8D949C\">1. </Run><Run>primo</Run></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\" Padding=\"14,0,0,0\" TextIndent=\"-14\"><Run FontFamily=\"" + Mono + "\" Foreground=\"#FF8D949C\">2. </Run><Run>secondo</Run></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\" Padding=\"14,0,0,0\" TextIndent=\"-14\"><Run FontFamily=\"" + Mono + "\" Foreground=\"#FF1F9D45\">☑ </Run>" +
        "<Run TextDecorations=\"Strikethrough\" Foreground=\"#FF8D949C\">fatta</Run></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\" BorderBrush=\"#FF8D949C\" BorderThickness=\"2,0,0,0\" Padding=\"12,0,0,0\" FontStyle=\"Italic\" Foreground=\"#FF6A737D\"><Run>citazione</Run></Paragraph>" +
        "<Paragraph Margin=\"0,6.5,0,6.5\" BorderBrush=\"#FFE2E2E4\" BorderThickness=\"0,0,0,1\" LineHeight=\"1\" />" +
        "<Paragraph Margin=\"0,0,0,0\" Background=\"#FFF1F1F3\" Foreground=\"#FFB4009E\" FontFamily=\"" + Mono + "\" Padding=\"12,8,12,8\">" +
        "<Run Foreground=\"#FF2E6BE6\">var</Run><Run> x = 1;</Run><LineBreak /><Run>return x;</Run></Paragraph>" +
        "<Paragraph Margin=\"0,0,0,0\"><Run>prima </Run><Hyperlink NavigateUri=\"ducknote://riquadro/aaaaaaaa?t=immagine&amp;h=200&amp;w=300\"><Run>Immagine</Run></Hyperlink><Run> dopo</Run></Paragraph>" +
        "<Table CellSpacing=\"0\"><Table.Columns><TableColumn Width=\"*\" /><TableColumn Width=\"*\" /></Table.Columns><TableRowGroup>" +
        "<TableRow><TableCell Background=\"#FFF4F4F5\" FontWeight=\"SemiBold\"><Paragraph><Run>Host</Run></Paragraph></TableCell><TableCell><Paragraph><Run>Stato</Run></Paragraph></TableCell></TableRow>" +
        "<TableRow><TableCell><Paragraph><Run>10.0.0.1</Run></Paragraph></TableCell><TableCell><Paragraph><Run>attivo</Run></Paragraph></TableCell></TableRow>" +
        "</TableRowGroup></Table>" +
        "</Section>";

    [Fact]
    public void A_wpf_sheet_is_read_line_by_line_with_its_kinds()
    {
        SheetDoc doc = SheetXaml.Read(Encoding.UTF8.GetBytes(Wpf));
        SheetLine[] lines = [.. doc.Blocks.OfType<SheetLine>()];

        Assert.Equal(
            [LineKind.Heading1, LineKind.Text, LineKind.Bullet, LineKind.Number, LineKind.Number, LineKind.Todo, LineKind.Quote, LineKind.Rule, LineKind.Code, LineKind.Text],
            lines.Select(line => line.Kind));
        Assert.Equal("voce", lines[2].Text);
        Assert.Equal("secondo", lines[4].Text);
        Assert.True(lines[5].Done);
        Assert.Equal("fatta", lines[5].Text);
        Assert.Equal("var x = 1;\nreturn x;", lines[8].Text);
        Assert.IsType<SheetTable>(doc.Blocks[^1]);
    }

    [Fact]
    public void Marks_and_colours_of_the_writer_are_kept_and_the_painted_ones_are_dropped()
    {
        SheetLine text = SheetXaml.Read(Encoding.UTF8.GetBytes(Wpf)).Blocks.OfType<SheetLine>().ElementAt(1);
        TextPiece[] pieces = [.. text.Pieces.OfType<TextPiece>()];

        // l'indirizzo era verde perche' attivo: e' testo normale, il colore lo rimette l'editor
        Assert.Contains(pieces, p => p.Text.Contains("192.168.1.1") && p.Colour is null && p.Marks == TextMarks.None);
        Assert.Contains(pieces, p => p.Text == "grassetto" && p.Marks == TextMarks.Bold);
        Assert.Contains(pieces, p => p.Text == "rosso" && p.Colour == "Red");
        Assert.Contains(pieces, p => p.Text == "evidenziato" && p.Marks == TextMarks.Highlight && p.Colour is null);
        Assert.Contains(pieces, p => p.Text == "code" && p.Marks == TextMarks.Code && p.Colour is null);
        Assert.Contains(pieces, p => p.Text == "sito" && p.Link == "https://example.com/" && p.Colour is null);
    }

    [Fact]
    public void Inset_placeholders_become_insets()
    {
        SheetLine line = SheetXaml.Read(Encoding.UTF8.GetBytes(Wpf)).Blocks.OfType<SheetLine>().Last();
        InsetPiece inset = Assert.Single(line.Pieces.OfType<InsetPiece>());

        Assert.Equal(InsetKind.Picture, inset.Address.Kind);
        Assert.Equal(300, inset.Address.Width);
        Assert.Equal("prima  dopo", line.Text);
    }

    [Fact]
    public void Writing_and_reading_again_changes_nothing()
    {
        SheetDoc first = SheetXaml.Read(Encoding.UTF8.GetBytes(Wpf));
        SheetDoc second = SheetXaml.Read(SheetXaml.Write(first));

        Assert.Equal(SheetJson.ToJson(first).ToJsonString(), SheetJson.ToJson(second).ToJsonString());
    }

    [Fact]
    public void The_editor_format_goes_and_comes_back_unchanged()
    {
        SheetDoc doc = SheetXaml.Read(Encoding.UTF8.GetBytes(Wpf));
        JsonObject json = SheetJson.ToJson(doc);
        SheetDoc back = SheetJson.FromJson(JsonNode.Parse(json.ToJsonString()));

        Assert.Equal(json.ToJsonString(), SheetJson.ToJson(back).ToJsonString());
    }

    [Fact]
    public void Lines_are_written_the_way_wpf_dresses_them()
    {
        SheetDoc doc = new();
        doc.Blocks.Add(SheetLine.Of(LineKind.Heading2, "Titolo"));
        doc.Blocks.Add(SheetLine.Of(LineKind.Number, "uno"));
        doc.Blocks.Add(SheetLine.Of(LineKind.Number, "due"));
        doc.Blocks.Add(new SheetLine(LineKind.Todo, [new TextPiece("fatto")], Done: true));
        string xaml = Encoding.UTF8.GetString(SheetXaml.Write(doc));

        Assert.Contains("FontSize=\"17\" FontWeight=\"SemiBold\"", xaml);
        Assert.Contains(">1. </Run><Run>uno", xaml);
        Assert.Contains(">2. </Run><Run>due", xaml);
        Assert.Contains("☑ </Run><Run TextDecorations=\"Strikethrough\"", xaml);
        Assert.StartsWith("<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\">", xaml);
    }

    [Fact]
    public void A_broken_sheet_reads_as_an_empty_page()
    {
        SheetDoc doc = SheetXaml.Read(Encoding.UTF8.GetBytes("<Section><Paragraph>non chiuso"));

        Assert.Single(doc.Blocks);
        Assert.True(doc.IsBlank);
    }

    [Fact]
    public void Headings_and_hosts_come_from_the_text()
    {
        SheetDoc doc = SheetXaml.Read(Encoding.UTF8.GetBytes(Wpf));

        Assert.Equal("DuckNote", Assert.Single(doc.Headings()).Text);
        Assert.Contains("192.168.1.1", doc.PlainText());
        Assert.Contains("10.0.0.1", doc.PlainText());
    }
}
