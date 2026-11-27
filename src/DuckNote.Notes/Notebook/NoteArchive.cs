using System.IO.Compression;
using System.Text;

namespace DuckNote.Notes.Notebook;

/// <summary>Una pagina come sta su disco: identificativo, nome dato a mano, XAML del documento.</summary>
public sealed record ArchivedPage(string Id, string Name, byte[] Xaml);

/// <summary>
/// Il file della nota: uno zip con una voce per pagina ("{id}.xaml"), i byte dei riquadri
/// sotto "riquadri/", le mappe della rete in "mappa.json" e "pagine.txt" con ordine e nomi
/// (id, tab, nome). Le note di prima delle pagine sono un XAML solo, non uno zip: si leggono
/// come una pagina senza nome. Le voci che questa versione non conosce (scritte da una versione
/// piu' nuova) si tengono e si riscrivono com'erano.
/// </summary>
public sealed class NoteArchive
{
    public const string IndexEntry = "pagine.txt";

    public const string MapEntry = "mappa.json";

    public const string GroupsEntry = "gruppi.json";

    public const string CalendarEntry = "calendario.json";

    /// <summary>Le voci di dati (JSON in UTF-8) che la nota porta oltre ai fogli.</summary>
    private static readonly string[] DataEntries = [MapEntry, GroupsEntry, CalendarEntry];

    public List<ArchivedPage> Pages { get; } = [];

    /// <summary>Byte dei riquadri, per nome di voce (vedi <see cref="InsetAddress.Entry"/>).</summary>
    public Dictionary<string, byte[]> Insets { get; } = new(StringComparer.Ordinal);

    private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);

    /// <summary>Le mappe della rete (vedi <see cref="Maps.NetworkMap"/>); null se la nota non ne ha.</summary>
    public byte[]? Map { get => Data(MapEntry); set => Data(MapEntry, value); }

    /// <summary>I gruppi di scansione (vedi <see cref="Groups.ScanGroups"/>); null se la nota non ne ha.</summary>
    public byte[]? Groups { get => Data(GroupsEntry); set => Data(GroupsEntry, value); }

    /// <summary>Il calendario (vedi <see cref="DuckNote.Notes.Calendar.Agenda"/>); null se la nota non ne ha.</summary>
    public byte[]? Calendar { get => Data(CalendarEntry); set => Data(CalendarEntry, value); }

    /// <summary>Le voci sconosciute, per nome: tornano nel file tali e quali.</summary>
    public Dictionary<string, byte[]> Extras { get; } = new(StringComparer.Ordinal);

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

            foreach ((string entry, byte[] bytes) in _data)
            {
                if (bytes.Length > 0)
                {
                    Write(zip, entry, bytes);
                }
            }

            foreach ((string entry, byte[] bytes) in Extras)
            {
                if (!IsKnown(entry))
                {
                    Write(zip, entry, bytes);
                }
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

            foreach (string entry in DataEntries)
            {
                archive.Data(entry, Read(zip, entry));
            }

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.StartsWith("riquadri/", StringComparison.Ordinal) && Read(zip, entry.FullName) is { } inset)
                {
                    archive.Insets[entry.FullName] = inset;
                }
                else if (!IsKnown(entry.FullName) && !entry.FullName.EndsWith('/') && Read(zip, entry.FullName) is { } extra)
                {
                    archive.Extras[entry.FullName] = extra;
                }
            }

            return archive.Pages.Count > 0 ? archive : null;
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Le voci che questa versione scrive da se': l'indice, le pagine (anche quelle rimaste
    /// fuori dall'indice, che sono avanzi), i riquadri e le mappe.
    /// </summary>
    private static bool IsKnown(string entry) =>
        entry == IndexEntry
        || DataEntries.Contains(entry)
        || entry.StartsWith("riquadri/", StringComparison.Ordinal)
        || (!entry.Contains('/') && entry.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

    private byte[]? Data(string entry) => _data.GetValueOrDefault(entry);

    private void Data(string entry, byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
        {
            _data[entry] = bytes;
        }
        else
        {
            _data.Remove(entry);
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
