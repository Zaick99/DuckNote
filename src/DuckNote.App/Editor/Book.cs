using System.Security.Cryptography;
using System.Windows.Documents;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Outline;

namespace DuckNote.App.Editor;

/// <summary>Una pagina della nota: il suo documento e il nome che le si e' dato a mano.</summary>
public sealed class Sheet(string id, FlowDocument document, string name = "")
{
    public string Id { get; } = id;

    public FlowDocument Document { get; } = document;

    /// <summary>
    /// Il nome lo mette una persona, col tasto destro; il primo titolo del testo non lo
    /// rinomina: una pagina che si rinomina mentre si scrive non si ritrova piu'.
    /// </summary>
    public string Name { get; set; } = PageIds.Clean(name);

    public string Title => PageIds.Title(Name);

    /// <summary>
    /// Sale a ogni modifica del testo: chi tiene da parte qualcosa di calcolato sul foglio (il
    /// testo, i titoli, gli indirizzi, il file da salvare) lo rifa' solo se e' cambiato.
    /// </summary>
    public int Version { get; private set; }

    public void Changed()
    {
        Version++;
        _text = null;
        _headings = null;
        _frames = null;
    }

    private InsetFrame[]? _frames;

    /// <summary>I riquadri (disegni, immagini, grafici) del foglio.</summary>
    public InsetFrame[] Frames => _frames ??= InsetRafts.Frames(Document);

    private string? _text;
    private IReadOnlyList<Heading>? _headings;
    private (int Version, byte[] Xaml, IReadOnlyList<Inset> Insets)? _photo;

    /// <summary>Il foglio come va nel file. Rifatto solo se il foglio e' cambiato.</summary>
    public (byte[] Xaml, IReadOnlyList<Inset> Insets) Photograph()
    {
        if (_photo is { } kept && kept.Version == Version)
        {
            return (kept.Xaml, kept.Insets);
        }

        using InsetRafts.Folding folded = InsetRafts.Fold(Document);
        byte[] xaml = Doc.ToXaml(Document);
        IReadOnlyList<Inset> insets = folded.Insets;
        _photo = (Version, xaml, insets);

        return (xaml, insets);
    }

    public string Text => _text ??= Doc.PlainText(Document);

    public IReadOnlyList<Heading> Headings() => _headings ??= ReadHeadings();

    private List<Heading> ReadHeadings()
    {
        List<Heading> found = [];
        Paragraph[] rows = Doc.Rows(Document);

        for (int i = 0; i < rows.Length; i++)
        {
            Look look = Looks.Of(rows[i]);

            if (look is Look.Heading1 or Look.Heading2 or Look.Heading3)
            {
                found.Add(new Heading(Doc.LineOf(rows[i]).Trim(), look - Look.Heading1 + 1, i));
            }
        }

        return found;
    }

    public OutlinePage Outline() => new(Id, Title, Headings(), Text);
}

/// <summary>Le pagine della nota e il loro passaggio da e verso il file (vedi <see cref="NoteArchive"/>).</summary>
public sealed class Book
{
    private readonly List<Sheet> _sheets = [];

    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>Il file letto era di prima delle pagine, o portava margini vecchi: va riscritto.</summary>
    public bool NeedsRewrite { get; private set; }

    /// <summary>Le mappe della rete (mappa.json), se la nota ne ha: si sostituiscono intere, mai sul posto.</summary>
    public byte[]? Map { get; set; }

    /// <summary>I gruppi di scansione (gruppi.json), se la nota ne ha: si sostituiscono interi.</summary>
    public byte[]? Groups { get; set; }

    /// <summary>Il calendario (calendario.json), se la nota ne ha: si sostituisce intero.</summary>
    public byte[]? Calendar { get; set; }

    /// <summary>Le voci del file scritte da una versione piu' nuova: si riscrivono tali e quali.</summary>
    public Dictionary<string, byte[]> Extras { get; } = new(StringComparer.Ordinal);

    public static Book Of(FlowDocument first)
    {
        Book book = new();
        book._sheets.Add(new Sheet(PageIds.Fresh(), first));

        return book;
    }

    public Sheet Add()
    {
        FlowDocument document = Doc.Empty();
        document.Blocks.Add(Doc.Row());
        Sheet sheet = new(PageIds.Fresh(), document);
        _sheets.Add(sheet);

        return sheet;
    }

    public bool Remove(Sheet sheet) => _sheets.Count > 1 && _sheets.Remove(sheet);

    public Sheet? ById(string id) => _sheets.FirstOrDefault(sheet => sheet.Id == id);

    public static Book? Load(byte[]? bytes)
    {
        if (NoteArchive.Load(bytes) is not { } archive)
        {
            return null;
        }

        Book book = new() { NeedsRewrite = archive.WasSingle };

        foreach (ArchivedPage page in archive.Pages)
        {
            if (Doc.FromXaml(page.Xaml) is not { } document)
            {
                continue;
            }

            InsetRafts.Unfold(document, address => archive.Insets.GetValueOrDefault(address.Entry));
            book.NeedsRewrite |= Flatten(document);
            Palette.Retint(document);
            book._sheets.Add(new Sheet(page.Id, document, page.Name));
        }

        book.Map = archive.Map;
        book.Groups = archive.Groups;
        book.Calendar = archive.Calendar;

        foreach ((string entry, byte[] value) in archive.Extras)
        {
            book.Extras[entry] = value;
        }

        return book._sheets.Count > 0 ? book : null;
    }

    /// <summary>La nota pronta da scrivere, tutta nel thread dell'interfaccia (solo a chiusura, blocco, cifratura).</summary>
    public byte[] Save() => Snapshot().Save();

    /// <summary>
    /// Fotografa la nota: i fogli diventano XAML qui, nel thread dell'interfaccia (un documento
    /// si legge solo da li'), ma solo quelli cambiati dall'ultima volta; gli altri riusano la
    /// fotografia che avevano. Comprimere (<see cref="NoteArchive.Save"/>), cifrare e scrivere
    /// si possono poi fare altrove.
    /// </summary>
    public NoteArchive Snapshot()
    {
        NoteArchive archive = new();

        foreach (Sheet sheet in _sheets)
        {
            (byte[] xaml, IReadOnlyList<Inset> insets) = sheet.Photograph();
            archive.Pages.Add(new ArchivedPage(sheet.Id, sheet.Name, xaml));

            foreach (Inset inset in insets)
            {
                archive.Insets[inset.Address.Entry] = inset.Bytes;
            }
        }

        archive.Map = Map;
        archive.Groups = Groups;
        archive.Calendar = Calendar;

        foreach ((string entry, byte[] bytes) in Extras)
        {
            archive.Extras[entry] = bytes;
        }

        return archive;
    }

    /// <summary>Via la nota dalla memoria: i byte dei riquadri azzerati, i documenti svuotati.</summary>
    public void Forget()
    {
        foreach (Sheet sheet in _sheets)
        {
            foreach (InsetFrame frame in InsetRafts.Frames(sheet.Document))
            {
                CryptographicOperations.ZeroMemory(frame.Inset.Bytes);
            }

            sheet.Document.Blocks.Clear();
        }

        foreach (byte[] bytes in Extras.Values)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        foreach (byte[]? data in (byte[]?[])[Map, Groups, Calendar])
        {
            if (data is not null)
            {
                CryptographicOperations.ZeroMemory(data);
            }
        }

        Extras.Clear();
        Map = null;
        Groups = null;
        Calendar = null;
        _sheets.Clear();
    }

    /// <summary>Le note vecchie avevano il margine automatico fra le righe: lo si toglie (non alle separazioni).</summary>
    private static bool Flatten(FlowDocument document)
    {
        bool changed = false;

        foreach (Paragraph row in Doc.Rows(document))
        {
            if (row.Margin != Doc.Flush && !Looks.Is(row, Look.Rule))
            {
                row.Margin = Doc.Flush;
                changed = true;
            }
        }

        return changed;
    }
}
