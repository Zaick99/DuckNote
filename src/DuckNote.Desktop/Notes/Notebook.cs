using System.Security.Cryptography;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Outline;
using DuckNote.Notes.Sheets;

namespace DuckNote.Desktop.Notes;

/// <summary>
/// Un foglio della nota: il suo documento (<see cref="SheetDoc"/>), il nome dato a mano e, per
/// non rifare il lavoro a ogni domanda, il testo, i titoli e lo XAML del file, ricalcolati solo
/// quando il foglio cambia.
/// </summary>
public sealed class Sheet(string id, SheetDoc doc, string name = "")
{
    private string? _text;
    private IReadOnlyList<Heading>? _headings;
    private byte[]? _xaml;

    public string Id { get; } = id;

    public SheetDoc Doc { get; private set; } = doc;

    public string Name { get; set; } = PageIds.Clean(name);

    public string Title => PageIds.Title(Name);

    /// <summary>Cresce a ogni modifica: chi ha fotografato il foglio sa se la foto e' vecchia.</summary>
    public int Version { get; private set; }

    public void Replace(SheetDoc doc)
    {
        Doc = doc;
        Version++;
        _text = null;
        _headings = null;
        _xaml = null;
    }

    public string Text => _text ??= Doc.PlainText();

    public IReadOnlyList<Heading> Headings() => _headings ??= Doc.Headings();

    /// <summary>Lo XAML del foglio, come lo scrive (e lo legge) la versione WPF.</summary>
    public byte[] Xaml => _xaml ??= SheetXaml.Write(Doc);

    public OutlinePage Outline() => new(Id, Title, Headings(), Text);
}

/// <summary>
/// I fogli della nota e i byte dei riquadri, e il loro passaggio da e verso il file della nota
/// (<see cref="NoteArchive"/>: uno zip con un XAML per foglio, gli stessi della versione WPF).
/// </summary>
public sealed class Notebook
{
    private readonly List<Sheet> _sheets = [];

    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>I byte dei riquadri, per voce dello zip (riquadri/id.tipo).</summary>
    public Dictionary<string, byte[]> Insets { get; } = new(StringComparer.Ordinal);

    /// <summary>Le mappe della rete (mappa.json), se la nota ne ha.</summary>
    public byte[]? Map { get; set; }

    /// <summary>Le voci del file scritte da una versione piu' nuova: si riscrivono tali e quali.</summary>
    public Dictionary<string, byte[]> Extras { get; } = new(StringComparer.Ordinal);

    /// <summary>Il file letto era di prima dei fogli: va riscritto.</summary>
    public bool NeedsRewrite { get; private set; }

    public static Notebook Of(SheetDoc first)
    {
        Notebook book = new();
        book._sheets.Add(new Sheet(PageIds.Fresh(), first));
        return book;
    }

    public Sheet Add()
    {
        Sheet sheet = new(PageIds.Fresh(), SheetDoc.Blank());
        _sheets.Add(sheet);
        return sheet;
    }

    public bool Remove(Sheet sheet) => _sheets.Count > 1 && _sheets.Remove(sheet);

    public Sheet? ById(string id) => _sheets.FirstOrDefault(sheet => sheet.Id == id);

    public int IndexOf(Sheet sheet) => _sheets.IndexOf(sheet);

    public void Move(Sheet sheet, int index)
    {
        if (_sheets.Remove(sheet))
        {
            _sheets.Insert(Math.Clamp(index, 0, _sheets.Count), sheet);
        }
    }

    public static Notebook? Load(byte[]? bytes)
    {
        if (NoteArchive.Load(bytes) is not { } archive)
        {
            return null;
        }

        Notebook book = new() { NeedsRewrite = archive.WasSingle };

        foreach (ArchivedPage page in archive.Pages)
        {
            book._sheets.Add(new Sheet(page.Id, SheetXaml.Read(page.Xaml), page.Name));
        }

        foreach ((string entry, byte[] value) in archive.Insets)
        {
            book.Insets[entry] = value;
        }

        foreach ((string entry, byte[] value) in archive.Extras)
        {
            book.Extras[entry] = value;
        }

        book.Map = archive.Map;

        return book._sheets.Count > 0 ? book : null;
    }

    /// <summary>
    /// La nota come va nel file. I riquadri che nessun foglio usa piu' restano fuori; i loro byte
    /// restano in memoria finche' la nota e' aperta, per un Annulla che li rimetta.
    /// </summary>
    public NoteArchive Snapshot()
    {
        NoteArchive archive = new();

        foreach (Sheet sheet in _sheets)
        {
            archive.Pages.Add(new ArchivedPage(sheet.Id, sheet.Name, sheet.Xaml));

            foreach (InsetAddress inset in sheet.Doc.Insets())
            {
                if (Insets.TryGetValue(inset.Entry, out byte[]? bytes))
                {
                    archive.Insets[inset.Entry] = bytes;
                }
            }
        }

        // la mappa si sostituisce intera (mai riscritta sul posto): la foto puo' tenerne i byte
        archive.Map = Map;

        foreach ((string entry, byte[] bytes) in Extras)
        {
            archive.Extras[entry] = bytes;
        }

        return archive;
    }

    public byte[] Save() => Snapshot().Save();

    /// <summary>Via la nota dalla memoria: i byte dei riquadri azzerati, i fogli dimenticati.</summary>
    public void Forget()
    {
        foreach (byte[] bytes in Insets.Values.Concat(Extras.Values))
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        if (Map is { } map)
        {
            CryptographicOperations.ZeroMemory(map);
        }

        Insets.Clear();
        Extras.Clear();
        Map = null;
        _sheets.Clear();
    }

    /// <summary>I byte di un riquadro, cercato per id (l'estensione dipende dal tipo).</summary>
    public byte[]? InsetBytes(string id) =>
        Insets.FirstOrDefault(pair => pair.Key.StartsWith($"riquadri/{id}.", StringComparison.Ordinal)).Value;
}

/// <summary>Le note che DuckNote scrive da se': quella di benvenuto e l'avviso di contenitore rovinato.</summary>
public static class Welcome
{
    public static SheetDoc Note() => Doc(
        (LineKind.Heading1, "DuckNote"),
        (LineKind.Text, "Note tecniche e scanner di rete nella stessa finestra."),
        (LineKind.Text, string.Empty),
        (LineKind.Text, "Scrivi un indirizzo e DuckNote lo controlla: il gateway 192.168.1.1, il NAS nas.casa.lan, " +
            "i DNS 1.1.1.1 e 1.0.0.1. Ogni indirizzo riconosciuto prende il colore del suo stato: verde " +
            "raggiungibile, rosso spento, grigio mai provato."),
        (LineKind.Text, string.Empty),
        (LineKind.Heading2, "Come si scrive"),
        (LineKind.Text, "Si scrive e si preme un tasto della barra: niente marcatori da ricordare. Grassetto, corsivo, " +
            "sottolineato, barrato, evidenziato, codice, titoli, elenchi, spunte, citazioni, tabelle, collegamenti."),
        (LineKind.Text, string.Empty),
        (LineKind.Todo, "provare una spunta, cliccandoci sopra"),
        (LineKind.Bullet, "una voce di elenco"),
        (LineKind.Quote, "Una citazione, per le cose dette da altri."),
        (LineKind.Text, string.Empty),
        (LineKind.Heading2, "Disegni, immagini e grafici"),
        (LineKind.Text, "Gli ultimi tasti della barra mettono nel testo un riquadro da disegno, un'immagine o un grafico. " +
            "Il testo gli gira attorno; i bordi si trascinano per cambiarne la misura."),
        (LineKind.Text, string.Empty));

    public static SheetDoc Damaged() => Doc(
        (LineKind.Heading1, "Il contenitore non si e' lasciato leggere"),
        (LineKind.Text, "La verifica di integrita' non torna: il file e' stato troncato, alterato, o scritto da una versione diversa."),
        (LineKind.Text, "La nota NON viene sovrascritta e il salvataggio resta sospeso, cosi' quel che c'e' dentro non va perso."),
        (LineKind.Text, "Accanto al contenitore c'e' una copia di poco precedente:"),
        (LineKind.Code, Path.Combine(DuckNote.Notes.Settings.AppPaths.Folder, "store.bin.bak")),
        (LineKind.Text, "Chiudi DuckNote, rinominala in store.bin e riapri."));

    private static SheetDoc Doc(params (LineKind Kind, string Text)[] lines)
    {
        SheetDoc doc = new();

        foreach ((LineKind kind, string text) in lines)
        {
            doc.Blocks.Add(SheetLine.Of(kind, text));
        }

        return doc;
    }
}
