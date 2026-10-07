using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed class NoteBook
{
    private const string Index = "pagine.txt";

    private readonly List<NotePage> _pages = [];

    public IReadOnlyList<NotePage> Pages => _pages;

    public int Count => _pages.Count;

    public static NoteBook Of(FlowDocument first) =>
        new() { _pages = { new NotePage { Id = NotePage.FreshId(), Document = first } } };

    public NotePage Add(Brush foreground)
    {
        NotePage page = new() { Id = NotePage.FreshId(), Document = NoteDocument.Empty(foreground) };
        page.Document.Blocks.Add(new Paragraph());
        _pages.Add(page);

        return page;
    }

    public bool Remove(NotePage page) => _pages.Count > 1 && _pages.Remove(page);

    public NotePage? ById(string id) => _pages.FirstOrDefault(page => page.Id == id);

    public void Move(NotePage page, int to)
    {
        int from = _pages.IndexOf(page);

        if (from < 0 || to < 0 || to >= _pages.Count || from == to)
        {
            return;
        }

        _pages.RemoveAt(from);
        _pages.Insert(to, page);
    }

    public byte[] Save()
    {
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder order = new();

            foreach (NotePage page in _pages)
            {
                order.Append(page.Id).Append('\t').Append(NotePage.Clean(page.Name)).Append('\n');

                using Stream entry = archive.CreateEntry($"{page.Id}.xaml", CompressionLevel.Optimal).Open();
                entry.Write(NoteDocument.ToXaml(page.Document));
            }

            using Stream index = archive.CreateEntry(Index, CompressionLevel.Optimal).Open();
            index.Write(Encoding.UTF8.GetBytes(order.ToString()));
        }

        return stream.ToArray();
    }

    public static NoteBook? Load(byte[] bytes, Brush foreground, out bool single)
    {
        single = !IsArchive(bytes);

        return single ? Lone(bytes, foreground) : Many(bytes, foreground);
    }

    private static bool IsArchive(byte[] bytes) => bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K';

    private static NoteBook? Lone(byte[] bytes, Brush foreground) =>
        NoteDocument.FromXaml(bytes, foreground) is { } document ? Of(document) : null;

    private static NoteBook? Many(byte[] bytes, Brush foreground)
    {
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read);

            NoteBook book = new();

            foreach ((string id, string name) in Order(archive))
            {
                if (archive.GetEntry($"{id}.xaml") is not { } entry)
                {
                    continue;
                }

                using Stream content = entry.Open();
                using MemoryStream raw = new();
                content.CopyTo(raw);

                if (NoteDocument.FromXaml(raw.ToArray(), foreground) is { } document)
                {
                    book._pages.Add(new NotePage { Id = id, Document = document, Name = name });
                }
            }

            return book.Count > 0 ? book : null;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or XamlParseException)
        {
            return null;
        }
    }

    private static List<(string Id, string Name)> Order(ZipArchive archive)
    {
        if (archive.GetEntry(Index) is not { } index)
        {
            return [];
        }

        using Stream content = index.Open();
        using StreamReader reader = new(content, Encoding.UTF8);

        List<(string, string)> listed = [];

        foreach (string line in reader.ReadToEnd().Split('\n'))
        {
            string[] parts = line.Split('\t', 2);
            string id = parts[0].Trim();

            if (NotePage.IsId(id))
            {
                listed.Add((id, parts.Length > 1 ? NotePage.Clean(parts[1]) : string.Empty));
            }
        }

        return listed;
    }
}
