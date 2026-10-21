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

    public string Text => Doc.PlainText(Document);

    public IReadOnlyList<Heading> Headings()
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

        return book._sheets.Count > 0 ? book : null;
    }

    public byte[] Save()
    {
        NoteArchive archive = new();

        foreach (Sheet sheet in _sheets)
        {
            using InsetRafts.Folding folded = InsetRafts.Fold(sheet.Document);

            archive.Pages.Add(new ArchivedPage(sheet.Id, sheet.Name, Doc.ToXaml(sheet.Document)));

            foreach (Inset inset in folded.Insets)
            {
                archive.Insets[inset.Address.Entry] = inset.Bytes;
            }
        }

        return archive.Save();
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
