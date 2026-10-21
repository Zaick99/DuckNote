using System.IO.Compression;
using System.Text;

namespace DuckNote.Notes.Notebook;

/// <summary>Una pagina come sta su disco: identificativo, nome dato a mano, XAML del documento.</summary>
public sealed record ArchivedPage(string Id, string Name, byte[] Xaml);

/// <summary>
/// Il file della nota: uno zip con una voce per pagina ("{id}.xaml"), i byte dei riquadri
/// sotto "riquadri/", e "pagine.txt" con ordine e nomi (id, tab, nome). Le note di prima delle
/// pagine sono un XAML solo, non uno zip: si leggono come una pagina senza nome.
/// </summary>
public sealed class NoteArchive
{
    public const string IndexEntry = "pagine.txt";

    public List<ArchivedPage> Pages { get; } = [];

    /// <summary>Byte dei riquadri, per nome di voce (vedi <see cref="InsetAddress.Entry"/>).</summary>
    public Dictionary<string, byte[]> Insets { get; } = new(StringComparer.Ordinal);

    /// <summary>Il file letto era una nota di una pagina sola, di prima che le pagine esistessero.</summary>
    public bool WasSingle { get; private init; }

    public static bool IsZip(byte[]? bytes) => bytes is { Length: > 1 } && bytes[0] == 'P' && bytes[1] == 'K';

    public byte[] Save()
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder index = new();

            foreach (ArchivedPage page in Pages)
            {
                index.Append(page.Id).Append('\t').Append(PageIds.Clean(page.Name)).Append('\n');
                Write(zip, $"{page.Id}.xaml", page.Xaml);
            }

            foreach ((string entry, byte[] bytes) in Insets)
            {
                Write(zip, entry, bytes);
            }

            Write(zip, IndexEntry, Encoding.UTF8.GetBytes(index.ToString()));
        }

        return stream.ToArray();
    }

    /// <summary>Null se i byte non sono una nota leggibile.</summary>
    public static NoteArchive? Load(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        if (!IsZip(bytes))
        {
            NoteArchive single = new() { WasSingle = true };
            single.Pages.Add(new ArchivedPage(PageIds.Fresh(), string.Empty, bytes));
            return single;
        }

        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);

            NoteArchive archive = new();

            foreach ((string id, string name) in Index(zip))
            {
                if (Read(zip, $"{id}.xaml") is { } xaml)
                {
                    archive.Pages.Add(new ArchivedPage(id, name, xaml));
                }
            }

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.StartsWith("riquadri/", StringComparison.Ordinal) && Read(zip, entry.FullName) is { } inset)
                {
                    archive.Insets[entry.FullName] = inset;
                }
            }

            return archive.Pages.Count > 0 ? archive : null;
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            return null;
        }
    }

    private static List<(string Id, string Name)> Index(ZipArchive zip)
    {
        if (Read(zip, IndexEntry) is not { } index)
        {
            return [];
        }

        List<(string, string)> listed = [];

        foreach (string line in Encoding.UTF8.GetString(index).Split('\n'))
        {
            string[] parts = line.Split('\t', 2);
            string id = parts[0].Trim();

            if (PageIds.IsValid(id))
            {
                listed.Add((id, parts.Length > 1 ? PageIds.Clean(parts[1]) : string.Empty));
            }
        }

        return listed;
    }

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        using Stream entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        entry.Write(bytes);
    }

    private static byte[]? Read(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return null;
        }

        using Stream content = entry.Open();
        using MemoryStream raw = new();
        content.CopyTo(raw);

        return raw.ToArray();
    }
}
