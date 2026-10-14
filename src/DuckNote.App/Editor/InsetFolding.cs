using System.Windows;
using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>
/// Il doppio volto di un riquadro: cornice viva quando si legge la nota,
/// segnaposto quando la nota va su disco.
///
/// Serve perche' un contenitore di oggetti non sopravvive al salvataggio in
/// XAML, mentre l'indirizzo di un Hyperlink si. Lo scambio avviene dentro la
/// riga, fra inline: la cornice galleggia su una zattera, larga quanto basta,
/// col testo che le gira attorno riga dopo riga, e due cornici nello stesso
/// paragrafo stanno in fila allineate. Al suo posto va un collegamento. E'
/// reversibile e rimette in pagina le stesse cornici di prima, cosi' un
/// salvataggio automatico non interrompe chi sta disegnando.
/// </summary>
public static class InsetFolding
{
    public static InsetBox[] Boxes(FlowDocument? document) =>
        [.. Frames(document).Select(frame => frame.Box)];

    /// <summary>
    /// Mette al posto di ogni cornice il suo segnaposto e raccoglie i byte da
    /// scrivere. Rimettere a posto e' compito di chi tiene il risultato: appena
    /// lo chiude tornano in pagina le stesse cornici, non delle copie.
    /// </summary>
    public static Folding Fold(FlowDocument? document)
    {
        List<Swap> swapped = [];

        foreach (Frame frame in Frames(document))
        {
            frame.Box.Harvest();

            Hyperlink mark = Placeholder(frame.Box.Inset);
            frame.Row.Inlines.InsertBefore(frame.Raft, mark);
            frame.Row.Inlines.Remove(frame.Raft);

            swapped.Add(new Swap(frame.Row, frame.Raft, mark, frame.Box.Inset));
        }

        return new Folding(swapped);
    }

    /// <summary>
    /// Rimette le cornici dove la nota salvata aveva lasciato i segnaposti. Un
    /// riquadro senza byte non diventa una cornice vuota: resta scritto che c'e'
    /// stato, perche' sparire in silenzio sarebbe peggio.
    /// </summary>
    public static InsetBox[] Unfold(
        FlowDocument? document,
        Func<Inset, byte[]?> bytesOf,
        EditorBrushes brushes)
    {
        List<InsetBox> raised = [];

        foreach (Paragraph row in LiveFormatter.RowsOf(document))
        {
            foreach (Hyperlink mark in row.Inlines.OfType<Hyperlink>().ToArray())
            {
                if (Inset.At(mark.NavigateUri) is not { } inset)
                {
                    continue;
                }

                if (bytesOf(inset) is not { } bytes)
                {
                    Lost(row, mark, inset, brushes);
                    continue;
                }

                inset.Bytes = bytes;
                InsetBox box = new(inset, brushes);

                row.Inlines.InsertBefore(mark, Raft(box));
                row.Inlines.Remove(mark);

                raised.Add(box);
            }
        }

        return [.. raised];
    }

    /// <summary>
    /// La zattera su cui galleggia la cornice: larga quanto il riquadro chiede,
    /// appoggiata a sinistra, col testo che le scorre attorno riga dopo riga.
    ///
    /// E' l'unico contenitore di un FlowDocument che faccia girare il testo di
    /// fianco a un oggetto: un InlineUIContainer e' una parola sola, quindi
    /// accanto ci sta una riga e le altre vanno sotto. Due zattere si allineano
    /// se stanno nello stesso paragrafo: in paragrafi diversi sono ancorate a
    /// righe diverse e restano sfasate.
    /// </summary>
    public static Floater Raft(InsetBox box) =>
        new(new BlockUIContainer(box) { Margin = NoteDocument.Flush })
        {
            Width = box.Inset.Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, InsetBox.Apart, 2),
            Padding = NoteDocument.Flush
        };

    public static Hyperlink Placeholder(Inset inset) =>
        new(new Run(inset.Title)) { NavigateUri = inset.Address };

    public static Paragraph Row(Inset inset)
    {
        Paragraph row = NoteDocument.Row();
        row.Inlines.Add(Placeholder(inset));

        return row;
    }

    /// <summary>Il riquadro scritto nel testo di cui non restano i byte.</summary>
    private static void Lost(Paragraph row, Hyperlink mark, Inset inset, EditorBrushes brushes)
    {
        row.Inlines.InsertBefore(mark, new Run($"[{inset.Title.ToLowerInvariant()} non piu' nella nota]")
        {
            Foreground = brushes.Tertiary,
            FontStyle = FontStyles.Italic
        });

        row.Inlines.Remove(mark);
    }

    private static Frame[] Frames(FlowDocument? document) =>
    [
        .. from row in LiveFormatter.RowsOf(document)
           from raft in row.Inlines.OfType<Floater>()
           from holder in raft.Blocks.OfType<BlockUIContainer>()
           where holder.Child is InsetBox
           select new Frame(row, raft, (InsetBox)holder.Child)
    ];

    private sealed record Frame(Paragraph Row, Floater Raft, InsetBox Box);

    internal sealed record Swap(Paragraph Row, Floater Raft, Hyperlink Mark, Inset Inset);

    public sealed class Folding : IDisposable
    {
        private readonly List<Swap> _swapped;

        internal Folding(List<Swap> swapped) => _swapped = swapped;

        public IReadOnlyList<Inset> Insets => [.. _swapped.Select(swap => swap.Inset)];

        public void Dispose()
        {
            foreach (Swap swap in _swapped)
            {
                swap.Row.Inlines.InsertBefore(swap.Mark, swap.Raft);
                swap.Row.Inlines.Remove(swap.Mark);
            }

            _swapped.Clear();
        }
    }
}
