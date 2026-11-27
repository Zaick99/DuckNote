using System.Text;
using DuckNote.Notes.Groups;
using DuckNote.Notes.Notebook;

namespace DuckNote.Notes.Tests;

public class ScanGroupsTests
{
    [Fact]
    public void A_host_is_added_to_a_group_once_at_the_end()
    {
        ScanGroup lab = new("glab", "Laboratorio", ["192.168.50.0/24"], Monitor: false);

        ScanGroup more = ScanGroups.WithTarget(lab, " 192.168.60.7 ");
        Assert.Equal(["192.168.50.0/24", "192.168.60.7"], more.Targets);
        Assert.Same(more, ScanGroups.WithTarget(more, "192.168.60.7"));
        Assert.Same(lab, ScanGroups.WithTarget(lab, "  "));
    }

    [Fact]
    public void Only_the_lines_that_name_the_host_can_be_taken_away()
    {
        ScanGroup office = new("gufficio", "Ufficio", ["192.168.10.0/24", "192.168.40.10", "NAS01.example.local"], Monitor: true);
        string[] nas = ["192.168.40.10", "nas01.example.local", ""];

        Assert.Equal(["192.168.40.10", "NAS01.example.local"], ScanGroups.Listed(office, nas));
        Assert.Equal(["192.168.10.0/24"], ScanGroups.WithoutHost(office, nas).Targets);
        // un host che sta nel gruppo per la sua rete non ha righe sue
        Assert.Empty(ScanGroups.Listed(office, ["192.168.10.20"]));
        Assert.Same(office, ScanGroups.WithoutHost(office, ["192.168.10.20"]));
    }

    [Fact]
    public void Groups_travel_inside_the_note_file()
    {
        ScanGroup office = new("gufficio", "Ufficio", ["192.168.10.0/24", "192.168.40.10", "nas01.example.local"], Monitor: true);
        ScanGroup lab = new("glab", "Laboratorio", ["192.168.50.1-20"], Monitor: false);

        NoteArchive archive = new();
        archive.Pages.Add(new ArchivedPage("aaa111", string.Empty, [1]));
        archive.Groups = ScanGroups.Write([office, lab]);

        IReadOnlyList<ScanGroup> back = ScanGroups.Read(NoteArchive.Load(archive.Save())!.Groups);

        Assert.Equal(2, back.Count);
        Assert.Equal(office.Targets, back[0].Targets);
        Assert.True(back[0].Monitor);
        Assert.False(back[1].Monitor);
        Assert.Equal("Laboratorio", back[1].Name);
    }

    [Fact]
    public void No_groups_means_no_entry_in_the_note() => Assert.Null(ScanGroups.Write([]));

    [Theory]
    [InlineData("")]
    [InlineData("non e' json")]
    [InlineData("""{ "groups": 5 }""")]
    public void A_broken_file_has_no_groups(string json) => Assert.Empty(ScanGroups.Read(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Written_groups_are_tidied_on_reading()
    {
        IReadOnlyList<ScanGroup> groups = ScanGroups.Read(Encoding.UTF8.GetBytes(
            """{ "groups": [ { "id": "nota", "name": "", "targets": [" 10.0.0.1 ", "10.0.0.1", ""] }, { "id": "X!", "name": "Due" } ] }"""));

        Assert.Equal(2, groups.Count);
        // "nota" e' il gruppo di serie: un gruppo salvato non puo' prendergli il posto
        Assert.NotEqual(ScanGroups.Note, groups[0].Id);
        Assert.Equal("Gruppo", groups[0].Name);
        Assert.Equal(["10.0.0.1"], groups[0].Targets);
        Assert.Empty(groups[1].Targets);
        Assert.NotEqual(groups[0].Id, groups[1].Id);
    }

    [Fact]
    public void Targets_can_be_written_one_per_line_or_with_commas() =>
        Assert.Equal(["192.168.1.0/24", "10.0.0.5", "nas.example.local", "192.168.2.10-50"],
            ScanGroups.Split("192.168.1.0/24\n10.0.0.5, nas.example.local;192.168.2.10-50\n10.0.0.5"));

    [Theory]
    [InlineData("192.168.1.0/24")]
    [InlineData("10.0.0.0/8")]
    [InlineData("192.168.1.10-50")]
    [InlineData("192.168.1.10-192.168.2.20")]
    [InlineData("10.0.0.5")]
    [InlineData("fe80::1")]
    [InlineData("nas.example.local")]
    public void Good_targets_have_no_problem(string target) => Assert.Null(ScanGroups.Problem(target));

    [Theory]
    [InlineData(".1-254")]
    [InlineData("192.168.1.0/33")]
    [InlineData("10.0.0.0/4")]
    [InlineData("192.168.1.50-10")]
    [InlineData("non va")]
    public void Bad_targets_say_what_is_wrong(string target) => Assert.NotNull(ScanGroups.Problem(target));

    [Fact]
    public void A_host_belongs_to_a_group_by_network_range_address_or_name()
    {
        TargetMatcher matcher = new(["192.168.10.0/24", "192.168.50.10-20", "10.0.0.5", "nas01.example.local"]);

        Assert.True(matcher.Matches("192.168.10.77"));
        Assert.False(matcher.Matches("192.168.11.1"));
        Assert.True(matcher.Matches("192.168.50.15"));
        Assert.False(matcher.Matches("192.168.50.21"));
        Assert.True(matcher.Matches("10.0.0.5"));
        Assert.True(matcher.Matches("192.168.40.20", "NAS01.example.local"));
        Assert.False(matcher.Matches(null, "altro.example.local"));
        Assert.Equal(["10.0.0.5", "nas01.example.local"], matcher.Singles);
        Assert.Equal(256 + 11, matcher.Addresses);
    }
}
