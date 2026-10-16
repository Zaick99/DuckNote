using System.Windows.Documents;
using System.Windows.Media;

namespace DuckNote.App.Editor;

public sealed class HostPaint(
    EditorBrushes brushes,
    Func<IReadOnlyDictionary<string, bool>> states,
    Func<string, bool>? setAside = null)
{
    public const string Mark = "host:";

    /// <summary>
    /// Colora gli indirizzi della riga. Quello che il cursore sta ancora
    /// scrivendo resta testo: <paramref name="caret"/> e' la sua posizione
    /// nella riga, e un -1 vuol dire "la riga e' finita, colora tutto".
    /// </summary>
    public void Paint(Paragraph row, int caret = -1)
    {
        foreach (Stretch stretch in Stretches(row))
        {
            Dress(row, stretch, caret);
        }
    }

    public void Recolour(Paragraph row)
    {
        foreach (Run run in row.Inlines.OfType<Run>())
        {
            if (Named(run) is not { } host)
            {
                continue;
            }

            if (Aside(host))
            {
                Undo(run);
                continue;
            }

            run.Foreground = brushes.ForHost(host, states());
        }
    }

    public static string? Named(Run run) =>
        run.Tag is string tag && tag.StartsWith(Mark, StringComparison.Ordinal) ? tag[Mark.Length..] : null;

    private bool Aside(string host) => setAside?.Invoke(host) ?? false;

    /// <summary>
    /// Veste gli indirizzi di un tratto, guardandolo tutto insieme: un
    /// indirizzo e' una parola della riga, non di un pezzo di riga. Cercarlo un
    /// pezzo per volta lasciava "welcome.zaick" vestito da host e ".net"
    /// vestito da testo, perche' la lettera scritta dopo nasce in un pezzo
    /// nuovo e nessuno rimetteva mai insieme i due.
    /// </summary>
    private void Dress(Paragraph row, Stretch stretch, int caret)
    {
        string text = stretch.Text;

        HostToken[] tokens =
        [
            .. HostTokens.Find(text)
                .Where(token => !Aside(token.Value))
                .Where(token => caret < 0 || !HostTokens.Touches(token, caret - stretch.At))
        ];

        // Niente da vestire e niente di vestito: unire pezzi di solo testo non
        // si vedrebbe, e muoverebbe il cursore per niente.
        if (tokens.Length == 0 && !stretch.Pieces.Any(piece => Named(piece) is not null))
        {
            return;
        }

        if (Cut(text, tokens).SequenceEqual(stretch.Pieces.Select(Shape)))
        {
            Freshen(stretch);
            return;
        }

        Recut(row, stretch, tokens);
    }

    /// <summary>I pezzi sono gia' quelli giusti: basta rinfrescare il colore.</summary>
    private void Freshen(Stretch stretch)
    {
        foreach (Run piece in stretch.Pieces)
        {
            if (Named(piece) is { } host)
            {
                piece.Foreground = brushes.ForHost(host, states());
            }
        }
    }

    /// <summary>Rifa' i pezzi del tratto come li vogliono gli indirizzi.</summary>
    private void Recut(Paragraph row, Stretch stretch, HostToken[] tokens)
    {
        Run look = Plain(stretch);
        Inline after = stretch.Pieces[^1];

        foreach ((string text, bool host) in Cut(stretch.Text, tokens))
        {
            Run piece = Copy(look, text);

            if (host)
            {
                Wear(piece, text);
            }

            after = Insert(row, after, piece);
        }

        foreach (Run old in stretch.Pieces)
        {
            row.Inlines.Remove(old);
        }
    }

    /// <summary>
    /// Il formato dei pezzi di solo testo del tratto: quello di un pezzo non
    /// vestito, se nel tratto ce n'e' uno. Se no quello di un vestito,
    /// spogliato: colore e carattere da host sono del pennello, non di chi ha
    /// scritto.
    /// </summary>
    private Run Plain(Stretch stretch)
    {
        if (stretch.Pieces.FirstOrDefault(piece => Named(piece) is null) is { } bare)
        {
            return Copy(bare, string.Empty);
        }

        Run look = Copy(stretch.Pieces[0], string.Empty);
        look.ClearValue(TextElement.FontFamilyProperty);
        look.Foreground = brushes.Text;

        return look;
    }

    /// <summary>Il tratto tagliato nei pezzi che deve avere: testo e host.</summary>
    private static IEnumerable<(string Text, bool Host)> Cut(string text, HostToken[] tokens)
    {
        int last = 0;

        foreach (HostToken token in tokens)
        {
            if (token.Start > last)
            {
                yield return (text[last..token.Start], false);
            }

            yield return (token.Value, true);
            last = token.Start + token.Length;
        }

        if (last < text.Length)
        {
            yield return (text[last..], false);
        }
    }

    private static (string Text, bool Host) Shape(Run piece) => (piece.Text, Named(piece) is not null);

    /// <summary>
    /// I tratti della riga: pezzi attaccati e vestiti allo stesso modo, ognuno
    /// con la sua posizione nella riga. Quel che non e' testo -- un
    /// collegamento, un riquadro -- chiude il tratto, perche' un indirizzo non
    /// ci passa attraverso.
    /// </summary>
    private static Stretch[] Stretches(Paragraph row)
    {
        List<Stretch> found = [];
        int at = 0;

        foreach (Inline inline in row.Inlines.ToArray())
        {
            if (inline is Run piece)
            {
                if (found.Count > 0 && found[^1].Follows(piece, at))
                {
                    found[^1].Add(piece);
                }
                else
                {
                    found.Add(new Stretch(at, piece));
                }
            }

            at += Length(inline);
        }

        return [.. found];
    }

    private static int Length(Inline inline) =>
        inline is Run run ? run.Text.Length : new TextRange(inline.ContentStart, inline.ContentEnd).Text.Length;

    /// <summary>
    /// Due pezzi vestiti allo stesso modo, e quindi da leggere come uno. Il
    /// colore e il carattere di un pezzo vestito da host sono del pennello: fra
    /// un host e il testo attorno non fanno differenza, mentre fra due pezzi di
    /// solo testo la fanno -- il grassetto e il monospazio li ha scelti chi
    /// scrive, e un indirizzo non li attraversa.
    /// </summary>
    private static bool Same(Run one, Run other)
    {
        bool painted = Named(one) is not null || Named(other) is not null;

        return one.FontWeight == other.FontWeight
            && one.FontStyle == other.FontStyle
            && Math.Abs(one.FontSize - other.FontSize) < 0.01
            && Equals(one.Background, other.Background)
            && Equals(one.TextDecorations, other.TextDecorations)
            && (painted
                || (Equals(one.FontFamily, other.FontFamily) && Equals(one.Foreground, other.Foreground)));
    }

    private void Undo(Run run)
    {
        run.Tag = null;
        run.ClearValue(TextElement.FontFamilyProperty);
        run.Foreground = brushes.Text;
    }

    private void Wear(Run run, string host)
    {
        run.Tag = Mark + host;
        run.Foreground = brushes.ForHost(host, states());
        run.FontFamily = new FontFamily(Dresser.MonoFonts);
    }

    private static Inline Insert(Paragraph row, Inline after, Run piece)
    {
        row.Inlines.InsertAfter(after, piece);

        return piece;
    }

    private static Run Copy(Run from, string text) => new(text)
    {
        FontWeight = from.FontWeight,
        FontStyle = from.FontStyle,
        FontFamily = from.FontFamily,
        FontSize = from.FontSize,
        Foreground = from.Foreground,
        Background = from.Background,
        TextDecorations = from.TextDecorations
    };

    /// <summary>
    /// Un tratto di riga: i pezzi attaccati che si leggono come uno, e da dove
    /// comincia nella riga.
    /// </summary>
    private sealed class Stretch(int at, Run first)
    {
        private readonly List<Run> _pieces = [first];

        public int At { get; } = at;

        public IReadOnlyList<Run> Pieces => _pieces;

        public string Text => string.Concat(_pieces.Select(piece => piece.Text));

        public bool Follows(Run piece, int at) => at == At + Text.Length && Same(_pieces[0], piece);

        public void Add(Run piece) => _pieces.Add(piece);
    }
}
