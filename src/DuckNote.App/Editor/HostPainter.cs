using System.Windows.Documents;
using System.Windows.Media;
using DuckNote.Notes.Hosts;

namespace DuckNote.App.Editor;

/// <summary>
/// Veste gli indirizzi scritti nel testo: monospazio e colore dello stato (verde raggiungibile,
/// rosso spento, grigio mai provato). Un indirizzo e' una parola della riga, non di un pezzo:
/// i pezzi di testo vestiti allo stesso modo si leggono insieme, cosi' "welcome.zaick" + ".net"
/// scritto dopo diventa un indirizzo solo. Ricolorare non sposta il testo.
/// </summary>
public sealed class HostPainter(Func<string, bool?> state, Func<string, bool> hidden)
{
    private const string Mark = "host:";

    public static string? HostOf(Run run) =>
        run.Tag is string tag && tag.StartsWith(Mark, StringComparison.Ordinal) ? tag[Mark.Length..] : null;

    /// <summary>
    /// Ridipinge la riga. <paramref name="caret"/> e' dove sta il cursore nella riga: l'indirizzo
    /// che sta ancora scrivendo resta testo. -1 vuol dire "riga finita, colora tutto".
    /// </summary>
    public void Paint(Paragraph row, int caret = -1)
    {
        foreach (Stretch stretch in Stretches(row))
        {
            Dress(row, stretch, caret);
        }
    }

    /// <summary>Solo i colori: i pezzi restano quelli che sono.</summary>
    public void Recolour(Paragraph row)
    {
        foreach (Run run in row.Inlines.OfType<Run>())
        {
            if (HostOf(run) is not { } host)
            {
                continue;
            }

            if (hidden(host))
            {
                Bare(run);
                continue;
            }

            run.Foreground = Ink(host);
        }
    }

    private Brush Ink(string host) => state(host) switch
    {
        true => Palette.Up,
        false => Palette.Down,
        _ => Palette.Unknown
    };

    private void Dress(Paragraph row, Stretch stretch, int caret)
    {
        string text = stretch.Text;

        HostToken[] tokens =
        [
            .. HostTokens.Find(text)
                .Where(token => !hidden(token.Value))
                .Where(token => caret < 0 || !HostTokens.Touches(token, caret - stretch.At))
        ];

        // Niente da vestire e niente di vestito: rifare i pezzi non si vedrebbe e sposterebbe il cursore.
        if (tokens.Length == 0 && stretch.Pieces.All(piece => HostOf(piece) is null))
        {
            return;
        }

        List<(string Text, bool Host)> wanted = [.. Cut(text, tokens)];

        if (wanted.SequenceEqual(stretch.Pieces.Select(piece => (piece.Text, HostOf(piece) is not null))))
        {
            foreach (Run piece in stretch.Pieces)
            {
                if (HostOf(piece) is { } host)
                {
                    piece.Foreground = Ink(host);
                }
            }

            return;
        }

        Run look = Template(stretch);
        Inline after = stretch.Pieces[^1];

        foreach ((string piece, bool host) in wanted)
        {
            Run fresh = Copy(look, piece);

            if (host)
            {
                fresh.Tag = Mark + piece;
                fresh.Foreground = Ink(piece);
                fresh.FontFamily = new FontFamily(Doc.MonoFonts);
            }

            row.Inlines.InsertAfter(after, fresh);
            after = fresh;
        }

        foreach (Run old in stretch.Pieces)
        {
            row.Inlines.Remove(old);
        }
    }

    /// <summary>Il formato del testo attorno: quello di un pezzo non vestito, o di uno vestito spogliato.</summary>
    private static Run Template(Stretch stretch)
    {
        if (stretch.Pieces.FirstOrDefault(piece => HostOf(piece) is null) is { } bare)
        {
            return Copy(bare, string.Empty);
        }

        Run look = Copy(stretch.Pieces[0], string.Empty);
        look.ClearValue(TextElement.FontFamilyProperty);
        look.ClearValue(TextElement.ForegroundProperty);

        return look;
    }

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
            last = token.End;
        }

        if (last < text.Length)
        {
            yield return (text[last..], false);
        }
    }

    private static void Bare(Run run)
    {
        run.Tag = null;
        run.ClearValue(TextElement.FontFamilyProperty);
        run.ClearValue(TextElement.ForegroundProperty);
    }

    private static Run Copy(Run from, string text)
    {
        Run copy = new(text)
        {
            FontWeight = from.FontWeight,
            FontStyle = from.FontStyle,
            FontSize = from.FontSize,
            TextDecorations = from.TextDecorations
        };

        foreach (System.Windows.DependencyProperty property in (System.Windows.DependencyProperty[])
                 [TextElement.FontFamilyProperty, TextElement.ForegroundProperty, TextElement.BackgroundProperty])
        {
            if (from.ReadLocalValue(property) is var value && value != System.Windows.DependencyProperty.UnsetValue)
            {
                copy.SetValue(property, value);
            }
        }

        return copy;
    }

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

            at += inline is Run run ? run.Text.Length : Doc.TextOf(inline).Length;
        }

        return [.. found];
    }

    /// <summary>
    /// Due pezzi vestiti allo stesso modo si leggono come uno. Colore e carattere di un pezzo
    /// vestito da host sono del pittore, quindi fra host e testo attorno non contano; fra due
    /// pezzi di testo si', perche' li ha scelti chi scrive.
    /// </summary>
    private static bool Same(Run one, Run other)
    {
        bool painted = HostOf(one) is not null || HostOf(other) is not null;

        return one.FontWeight == other.FontWeight
            && one.FontStyle == other.FontStyle
            && Math.Abs(one.FontSize - other.FontSize) < 0.01
            && Equals(one.Background, other.Background)
            && Equals(one.TextDecorations, other.TextDecorations)
            && (painted || (Equals(one.FontFamily, other.FontFamily) && Equals(one.Foreground, other.Foreground)));
    }

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
