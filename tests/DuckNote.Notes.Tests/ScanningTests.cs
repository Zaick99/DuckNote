using System.Text;
using DuckNote.Notes.Outline;
using DuckNote.Notes.Scanning;
using DuckNote.Notes.Security;
using DuckNote.Notes.Settings;

namespace DuckNote.Notes.Tests;

public class ScanningTests
{
    private static readonly Dictionary<string, string> Nas = new()
    {
        ["ip"] = "192.168.1.20",
        ["nome"] = "nas.home.lan",
        ["porte"] = "22, 80, 2222",
        ["os"] = "Linux / Unix / macOS",
        ["produttore"] = "Synology"
    };

    [Theory]
    [InlineData("", true)]
    [InlineData("synology", true)]
    [InlineData("porta:22", true)]
    [InlineData("porta:2", false)]
    [InlineData("ip:192.168 os:linux", true)]
    [InlineData("ip:10.", false)]
    [InlineData("os:\"unix / mac\"", true)]
    public void The_table_filter_reads_free_text_and_fields(string query, bool expected) =>
        Assert.Equal(expected, HostQuery.Parse(query).Matches(Nas));

    [Fact]
    public void Csv_quotes_what_needs_it_and_defuses_formulas()
    {
        string text = Encoding.UTF8.GetString(CsvTable.Write(["a", "b"], [["x;y", "=1+1"]]));

        Assert.StartsWith("\uFEFF", text);
        Assert.Contains("\"x;y\";'=1+1", text);
    }

    [Fact]
    public void The_last_scan_round_trips_and_drops_empty_fields()
    {
        string json = LastScanFile.Write([new Dictionary<string, string> { ["IP"] = "10.0.0.1", ["Mac"] = "" }]);
        IReadOnlyList<IReadOnlyDictionary<string, string>> back = LastScanFile.Read(json);

        Assert.Single(back);
        Assert.False(back[0].ContainsKey("Mac"));
        Assert.Empty(LastScanFile.Read("non json"));
    }

    [Fact]
    public void Longer_passwords_from_wider_alphabets_score_higher()
    {
        int weak = PasswordStrength.Bits("abc"u8, 3);
        int strong = PasswordStrength.Bits("correct-Horse-7-battery"u8, 23);

        Assert.True(strong > 85);
        Assert.True(weak < 40);
        Assert.Equal("Red", PasswordStrength.Describe(weak).Token);
    }

    [Fact]
    public void Settings_survive_a_round_trip_and_hand_edits_are_clamped()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        string file = Path.Combine(folder, "settings.json");

        try
        {
            AppSettings settings = new() { MonitorIntervalSec = 5, Ports = "22, 80, 22, 99999", Theme = "blu" };
            Assert.True(settings.Save(file));

            AppSettings back = AppSettings.Load(file);

            Assert.Equal(10, back.MonitorIntervalSec);
            Assert.Equal("light", back.Theme);
            Assert.Equal([22, 80], back.PortList());
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Outline_nests_second_level_under_the_first_and_keeps_orphans()
    {
        OutlinePage page = new("p1", "Rete", [new Heading("Orfano", 2, 0), new Heading("Sezione", 1, 1), new Heading("Dentro", 2, 2), new Heading("Dettaglio", 3, 3)], "");
        IReadOnlyList<OutlineRow> rows = OutlineTree.Build([page], "p1", null, new Dictionary<string, bool>());

        Assert.Equal(["Rete", "Orfano", "Sezione", "Dentro"], rows.Select(r => r.Title));
        Assert.Equal([0, 1, 1, 2], rows.Select(r => r.Depth));
        Assert.Equal([0, 2, 1, 2], rows.Select(r => r.Level));
        Assert.True(rows[0].IsCurrent);
    }

    [Fact]
    public void Outline_filter_counts_hits_inside_a_page_body()
    {
        OutlinePage page = new("p1", "Rete", [], "il nas e il NAS di nuovo");
        IReadOnlyList<OutlineRow> rows = OutlineTree.Build([page], null, "nas", new Dictionary<string, bool>());

        Assert.Equal(2, Assert.Single(rows).Hits);
        Assert.Empty(OutlineTree.Build([page], null, "router", new Dictionary<string, bool>()));
    }

    [Fact]
    public void A_folded_page_hides_its_headings()
    {
        OutlinePage page = new("p1", "Rete", [new Heading("Sezione", 1, 0)], "");
        IReadOnlyList<OutlineRow> rows = OutlineTree.Build([page], null, null, new Dictionary<string, bool> { ["p:p1"] = false });

        OutlineRow only = Assert.Single(rows);
        Assert.True(only.HasChildren);
        Assert.False(only.IsOpen);
    }
}
