using System.Text;
using DuckNote.Notes.Charts;
using DuckNote.Notes.Links;
using DuckNote.Notes.Notebook;

namespace DuckNote.Notes.Tests;

public class NotebookTests
{
    [Fact]
    public void An_archive_keeps_page_order_names_and_inset_bytes()
    {
        NoteArchive archive = new();
        archive.Pages.Add(new ArchivedPage("aaa111", "Rete di casa", [1, 2, 3]));
        archive.Pages.Add(new ArchivedPage("bbb222", string.Empty, [4]));
        archive.Insets["riquadri/ccc333.disegno"] = [9, 9];

        NoteArchive back = NoteArchive.Load(archive.Save())!;

        Assert.Equal(["aaa111", "bbb222"], back.Pages.Select(p => p.Id));
        Assert.Equal("Rete di casa", back.Pages[0].Name);
        Assert.Equal([1, 2, 3], back.Pages[0].Xaml);
        Assert.Equal([9, 9], back.Insets["riquadri/ccc333.disegno"]);
        Assert.False(back.WasSingle);
    }

    [Fact]
    public void A_note_from_before_pages_is_read_as_one_unnamed_page()
    {
        byte[] xaml = Encoding.UTF8.GetBytes("<Section xmlns=\"x\"/>");
        NoteArchive back = NoteArchive.Load(xaml)!;

        Assert.True(back.WasSingle);
        Assert.Single(back.Pages);
        Assert.Equal(xaml, back.Pages[0].Xaml);
    }

    [Fact]
    public void A_broken_zip_is_not_a_note() => Assert.Null(NoteArchive.Load([(byte)'P', (byte)'K', 1, 2, 3]));

    [Fact]
    public void Page_names_lose_control_characters() =>
        Assert.Equal("Rete casa", PageIds.Clean("\tRete\u0007 casa\n"));

    [Fact]
    public void An_inset_address_round_trips_and_clamps_its_size()
    {
        InsetAddress inset = new("abc123", InsetKind.Chart, 5000, 10);
        InsetAddress back = InsetAddress.Parse(inset.ToUri())!;

        Assert.Equal(InsetKind.Chart, back.Kind);
        Assert.Equal(InsetAddress.MostHeight, back.Height);
        Assert.Equal(InsetAddress.LeastWidth, back.Width);
        Assert.Equal("riquadri/abc123.grafico", back.Entry);
    }

    [Fact]
    public void A_written_inset_address_from_the_old_app_is_read()
    {
        InsetAddress back = InsetAddress.Parse(new Uri("ducknote://riquadro/0a1b2c3d4e5f?t=disegno&h=230&w=360"))!;

        Assert.Equal(InsetKind.Drawing, back.Kind);
        Assert.Equal(230, back.Height);
    }

    [Fact]
    public void A_real_link_is_not_an_inset() => Assert.Null(InsetAddress.Parse(new Uri("https://example.com/x")));

    [Fact]
    public void A_chart_reads_its_header_and_points()
    {
        ChartSpec chart = ChartSpec.Read(ChartSpec.Example.Replace("barre", "torta"));

        Assert.Equal(ChartShape.Pie, chart.Shape);
        Assert.Equal("Tempo di risposta (ms)", chart.Title);
        Assert.Equal(["gateway", "nas", "dns pubblico"], chart.Points.Select(p => p.Label));
        Assert.Equal(27, chart.Tallest);
    }

    [Fact]
    public void A_title_with_a_number_is_still_a_title() =>
        Assert.Empty(ChartSpec.Read("titolo: Latenza 2024").Points);

    [Fact]
    public void A_decimal_comma_is_accepted() => Assert.Equal(1.5, ChartSpec.Read("ritardo 1,5").Points[0].Value);

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("chi@example.com", "mailto:chi@example.com")]
    public void Addresses_are_tidied(string written, string expected) =>
        Assert.Equal(expected, WebAddress.Parse(written)!.ToString());

    [Fact]
    public void Other_schemes_are_refused() => Assert.Null(WebAddress.Parse("file:///C:/Windows/system32/calc.exe"));
}
