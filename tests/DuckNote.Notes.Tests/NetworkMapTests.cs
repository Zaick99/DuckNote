using System.IO.Compression;
using System.Text;
using DuckNote.Notes.Maps;
using DuckNote.Notes.Notebook;

namespace DuckNote.Notes.Tests;

public class NetworkMapTests
{
    private const string Doc = """
        {
          "v": 1,
          "current": "mufficio",
          "maps": [
            {
              "id": "mufficio", "name": "Ufficio",
              "nodes": [
                { "id": "nrouter", "kind": "router", "name": "Router", "host": "192.168.1.1", "x": 0, "y": 0 },
                { "id": "nnas", "kind": "nas", "name": "NAS", "host": " nas.casa.lan ", "x": 0, "y": 120 },
                { "id": "nsw", "kind": "switch", "name": "Switch", "host": "", "x": 0, "y": 240 },
                { "id": "nbad", "kind": "pc", "name": "Scritto male", "host": "non e' un host", "x": 0, "y": 360 },
                { "id": "ndup", "kind": "pc", "name": "Doppio", "host": "192.168.1.1", "x": 0, "y": 480 }
              ],
              "links": [ { "id": "lone", "a": "nrouter", "b": "nsw", "kind": "fiber", "speed": "10G" } ],
              "zones": []
            },
            {
              "id": "mcasa", "name": "Casa",
              "nodes": [ { "id": "ntv", "kind": "iot", "name": "TV", "host": "10.0.0.9", "x": 0, "y": 0 } ],
              "links": [], "zones": []
            }
          ]
        }
        """;

    [Fact]
    public void The_hosts_of_every_map_are_read_once_each()
    {
        byte[] json = NetworkMap.Clean(Doc)!;

        Assert.Equal(["192.168.1.1", "nas.casa.lan", "10.0.0.9"], NetworkMap.Hosts(json));
        Assert.Equal(6, NetworkMap.Devices(json));
        Assert.False(NetworkMap.IsBlank(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("non e' json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "maps": 5 }""")]
    [InlineData("""{ "v": 1 }""")]
    public void What_is_not_a_map_document_is_refused(string json) => Assert.Null(NetworkMap.Clean(json));

    [Fact]
    public void A_map_document_is_kept_compact()
    {
        byte[] json = NetworkMap.Clean(Doc)!;
        string text = Encoding.UTF8.GetString(json);

        Assert.DoesNotContain("\n", text);
        Assert.StartsWith("{\"v\":1,", text);
    }

    [Fact]
    public void The_empty_map_the_page_starts_with_is_blank()
    {
        Assert.True(NetworkMap.IsBlank(NetworkMap.Clean("""{ "v": 1, "current": "mx", "maps": [ { "id": "mx", "name": "Rete", "nodes": [], "links": [], "zones": [] } ] }""")));
        Assert.True(NetworkMap.IsBlank(null));
        // due mappe, anche vuote, sono una scelta di chi scrive: si tengono
        Assert.False(NetworkMap.IsBlank(NetworkMap.Clean("""{ "maps": [ { "nodes": [] }, { "nodes": [] } ] }""")));
        Assert.False(NetworkMap.IsBlank(NetworkMap.Clean("""{ "maps": [ { "nodes": [], "zones": [ { "id": "zz1" } ] } ] }""")));
    }

    [Fact]
    public void Broken_bytes_have_no_hosts() => Assert.Empty(NetworkMap.Hosts(Encoding.UTF8.GetBytes("{ rotto")));

    [Fact]
    public void The_map_travels_inside_the_note_file()
    {
        NoteArchive archive = new();
        archive.Pages.Add(new ArchivedPage("aaa111", "Rete", [1, 2, 3]));
        archive.Map = NetworkMap.Clean(Doc);

        NoteArchive back = NoteArchive.Load(archive.Save())!;

        Assert.Equal(archive.Map, back.Map);
        Assert.Empty(back.Extras);
    }

    [Fact]
    public void A_note_without_a_map_has_no_map_entry()
    {
        NoteArchive archive = new();
        archive.Pages.Add(new ArchivedPage("aaa111", string.Empty, [1]));

        byte[] saved = archive.Save();

        Assert.Null(NoteArchive.Load(saved)!.Map);
        using ZipArchive zip = new(new MemoryStream(saved), ZipArchiveMode.Read);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName == NoteArchive.MapEntry);
    }

    [Fact]
    public void Entries_from_a_newer_version_survive_a_save()
    {
        NoteArchive archive = new();
        archive.Pages.Add(new ArchivedPage("aaa111", string.Empty, [1]));
        archive.Extras["futuro/qualcosa.bin"] = [7, 7, 7];
        archive.Extras["altro.json"] = [8];

        NoteArchive once = NoteArchive.Load(archive.Save())!;
        NoteArchive twice = NoteArchive.Load(once.Save())!;

        Assert.Equal([7, 7, 7], twice.Extras["futuro/qualcosa.bin"]);
        Assert.Equal([8], twice.Extras["altro.json"]);
        Assert.Single(twice.Pages);
    }

    [Fact]
    public void Leftover_pages_and_known_entries_are_not_extras()
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, NoteArchive.IndexEntry, "aaa111\t\n");
            Add(zip, "aaa111.xaml", "<Section/>");
            // una pagina che l'indice non nomina piu' e' un avanzo, non una voce da tenere
            Add(zip, "zzz999.xaml", "<Section/>");
            Add(zip, "riquadri/abc.disegno", "x");
            Add(zip, NoteArchive.MapEntry, """{"maps":[]}""");
        }

        NoteArchive back = NoteArchive.Load(stream.ToArray())!;

        Assert.Empty(back.Extras);
        Assert.Single(back.Insets);
        Assert.NotNull(back.Map);
    }

    [Fact]
    public void The_page_carries_the_bundle_and_the_art()
    {
        string html = MapPage.Html([1, 2, 3]);

        Assert.Contains("window.dm", html);
        Assert.Contains(".dm-canvas", html);
        Assert.Contains("--empty-art:url(data:image/png;base64,AQID)", html);
        Assert.DoesNotContain("<script src", html);
    }

    [Fact]
    public void Theme_colours_go_to_the_page_as_rgba()
    {
        Dictionary<string, string> light = MapPage.ThemeCss("light");
        Dictionary<string, string> dark = MapPage.ThemeCss("dark");

        Assert.Equal("#FF8C00FF", light["Accent"]);
        Assert.Equal("#FF9F2EFF", dark["Accent"]);
        Assert.Equal(light["BgContent"], light["BgContentGlass"]);
        Assert.Equal("#11223344", MapPage.Rgba("#44112233"));
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using Stream entry = zip.CreateEntry(name).Open();
        entry.Write(Encoding.UTF8.GetBytes(text));
    }
}
