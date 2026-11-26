using System.Text;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Outline;

namespace DuckNote.Notes.Sheets;

/// <summary>
/// Il tipo di una riga del foglio. La nota e' fatta di righe, ognuna con il suo tipo: un
/// elenco e' una fila di righe "voce", una citazione di righe "citazione". Le stesse righe
/// della versione WPF (vedi Looks/Styler), quindi lo stesso file.
/// </summary>
public enum LineKind
{
    Text,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    Rule,
    Code,
    Bullet,
    Number,
    Todo
}

/// <summary>I segni che si mettono su un pezzo di testo.</summary>
[Flags]
public enum TextMarks
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strike = 8,
    Highlight = 16,
    Code = 32
}

/// <summary>Un pezzo di riga: testo con i suoi segni, un a capo dentro la riga o un riquadro.</summary>
public abstract record Piece;

/// <summary>
/// Testo con i suoi segni. <paramref name="Colour"/> e' un token del tema (Red, Blue...), mai un
/// colore scritto: cambia da solo con chiaro e scuro. <paramref name="Link"/> e' l'indirizzo di
/// un collegamento.
/// </summary>
public sealed record TextPiece(string Text, TextMarks Marks = TextMarks.None, string? Colour = null, string? Link = null) : Piece;

/// <summary>Un a capo dentro la stessa riga (Maiusc+Invio nel codice, testo incollato).</summary>
public sealed record BreakPiece : Piece;

/// <summary>Un riquadro (disegno, immagine, grafico) in mezzo al testo; i byte stanno nello zip.</summary>
public sealed record InsetPiece(InsetAddress Address) : Piece;

public abstract record SheetBlock;

/// <summary>Una riga: il suo tipo, i suoi pezzi e, per una spunta, se e' fatta.</summary>
public sealed record SheetLine(LineKind Kind, IReadOnlyList<Piece> Pieces, bool Done = false) : SheetBlock
{
    public static SheetLine Empty(LineKind kind = LineKind.Text) => new(kind, []);

    public static SheetLine Of(LineKind kind, string text) => new(kind, text.Length == 0 ? [] : [new TextPiece(text)]);

    /// <summary>Il testo della riga: gli a capo interni come '\n', i riquadri come niente.</summary>
    public string Text
    {
        get
        {
            StringBuilder text = new();

            foreach (Piece piece in Pieces)
            {
                switch (piece)
                {
                    case TextPiece written:
                        text.Append(written.Text);
                        break;
                    case BreakPiece:
                        text.Append('\n');
                        break;
                }
            }

            return text.ToString();
        }
    }
}

/// <summary>Una tabella: righe di celle, ogni cella le sue righe di testo. La prima riga e' l'intestazione.</summary>
public sealed record SheetTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<SheetLine>>> Rows) : SheetBlock
{
    public int Columns => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);
}

/// <summary>
/// Un foglio della nota fuori da qualunque editor: si legge dal file (<see cref="SheetXaml"/>),
/// si manda all'editor (<see cref="SheetJson"/>) e torna indietro, si scrive di nuovo nel file.
/// </summary>
public sealed class SheetDoc
{
    public List<SheetBlock> Blocks { get; } = [];

    public static SheetDoc Blank()
    {
        SheetDoc doc = new();
        doc.Blocks.Add(SheetLine.Empty());
        return doc;
    }

    /// <summary>Tutte le righe in ordine di lettura, anche quelle dentro le tabelle.</summary>
    public IEnumerable<SheetLine> Lines()
    {
        foreach (SheetBlock block in Blocks)
        {
            switch (block)
            {
                case SheetLine line:
                    yield return line;
                    break;

                case SheetTable table:
                    foreach (SheetLine inner in table.Rows.SelectMany(row => row).SelectMany(cell => cell))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    /// <summary>Il testo del foglio, una riga per riga: e' dove si cercano gli indirizzi.</summary>
    public string PlainText() => string.Join('\n', Lines().Select(line => line.Text));

    /// <summary>I titoli H1, H2 e H3, nell'ordine; l'indice e' la posizione fra i titoli.</summary>
    public IReadOnlyList<Heading> Headings()
    {
        List<Heading> found = [];

        foreach (SheetLine line in Lines())
        {
            if (line.Kind is LineKind.Heading1 or LineKind.Heading2 or LineKind.Heading3)
            {
                found.Add(new Heading(line.Text.Trim(), line.Kind - LineKind.Heading1 + 1, found.Count));
            }
        }

        return found;
    }

    public IEnumerable<InsetAddress> Insets() =>
        Lines().SelectMany(line => line.Pieces).OfType<InsetPiece>().Select(piece => piece.Address);

    /// <summary>Il foglio non dice niente: nessun testo, nessuna tabella, nessun riquadro.</summary>
    public bool IsBlank =>
        !Blocks.OfType<SheetTable>().Any() && !Insets().Any() && Lines().All(line => line.Text.Trim().Length == 0 && line.Kind != LineKind.Rule);

    public int Words() => PlainText().Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}
