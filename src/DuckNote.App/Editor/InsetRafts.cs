using System.Windows;
using System.Windows.Documents;
using DuckNote.Notes.Notebook;

namespace DuckNote.App.Editor;

/// <summary>
/// Il doppio volto di un riquadro: cornice viva mentre si legge, collegamento segnaposto quando
/// la nota va su disco (un contenitore di oggetti non sopravvive al salvataggio in XAML,
/// l'indirizzo di un collegamento si'). La cornice galleggia su una zattera (Floater) larga
/// quanto il riquadro, col testo che le gira attorno; due zattere nella stessa riga stanno in
/// fila, allineate. Lo scambio e' reversibile: un salvataggio non disturba chi sta disegnando.
/// </summary>
public static class InsetRafts
{
    public static InsetFrame[] Frames(FlowDocument? document) =>
    [
        .. from row in Doc.Rows(document)
           from raft in row.Inlines.OfType<Floater>()
           from holder in raft.Blocks.OfType<BlockUIContainer>()
           where holder.Child is InsetFrame
           select (InsetFrame)holder.Child
    ];

    public static Floater Raft(InsetFrame frame) =>
        new(new BlockUIContainer(frame) { Margin = Doc.Flush })
        {
            Width = frame.Inset.Address.Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, InsetFrame.Apart, 2),
            Padding = Doc.Flush
        };

    /// <summary>
    /// Mette i segnaposti al posto delle cornici e raccoglie i riquadri da salvare. Chiudendo il
    /// risultato tornano in pagina le stesse cornici di prima, non delle copie.
    /// </summary>
    public static Folding Fold(FlowDocument document)
    {
        List<(Paragraph Row, Floater Raft, Hyperlink Mark, Inset Inset)> swapped = [];

        foreach (Paragraph row in Doc.Rows(document))
        {
            foreach (Floater raft in row.Inlines.OfType<Floater>().ToArray())
            {
                if (raft.Blocks.FirstBlock is not BlockUIContainer { Child: InsetFrame frame })
                {
                    continue;
                }

                frame.Harvest();
                Hyperlink mark = Placeholder(frame.Inset);
                row.Inlines.InsertBefore(raft, mark);
                row.Inlines.Remove(raft);
                swapped.Add((row, raft, mark, frame.Inset));
            }
        }

        return new Folding(swapped);
    }

    /// <summary>
    /// Rimette le cornici al posto dei segnaposti. Un riquadro senza byte non diventa una cornice
    /// vuota: resta scritto che c'era, perche' sparire in silenzio sarebbe peggio.
    /// </summary>
    public static InsetFrame[] Unfold(FlowDocument document, Func<InsetAddress, byte[]?> bytesOf)
    {
        List<InsetFrame> raised = [];

        foreach (Paragraph row in Doc.Rows(document))
        {
            foreach (Hyperlink mark in row.Inlines.OfType<Hyperlink>().ToArray())
            {
                if (InsetAddress.Parse(mark.NavigateUri) is not { } address)
                {
                    continue;
                }

                if (bytesOf(address) is not { } bytes)
                {
                    Run lost = new($"[{address.Title.ToLowerInvariant()} non piu' nella nota]") { FontStyle = FontStyles.Italic };
                    lost.SetResourceReference(TextElement.ForegroundProperty, "LabelTertiary");
                    row.Inlines.InsertBefore(mark, lost);
                    row.Inlines.Remove(mark);
                    continue;
                }

                InsetFrame frame = new(new Inset(address, bytes));
                row.Inlines.InsertBefore(mark, Raft(frame));
                row.Inlines.Remove(mark);
                raised.Add(frame);
            }
        }

        return [.. raised];
    }

    public static Hyperlink Placeholder(Inset inset) => new(new Run(inset.Title)) { NavigateUri = inset.Address.ToUri() };

    /// <summary>
    /// Piu' riquadri nella stessa riga si spartiscono la larghezza in parti uguali, allineati.
    /// Ognuno porta il suo stacco a destra, quindi lo stacco si toglie da ciascuno.
    /// </summary>
    public static void Share(Paragraph row, double column)
    {
        Floater[] side = [.. row.Inlines.OfType<Floater>()];

        if (side.Length < 2)
        {
            return;
        }

        double each = Math.Max(InsetAddress.LeastWidth, column / side.Length - InsetFrame.Apart);

        foreach (Floater raft in side)
        {
            raft.Width = each;
        }
    }

    public sealed class Folding(List<(Paragraph Row, Floater Raft, Hyperlink Mark, Inset Inset)> swapped) : IDisposable
    {
        public IReadOnlyList<Inset> Insets => [.. swapped.Select(swap => swap.Inset)];

        public void Dispose()
        {
            foreach ((Paragraph row, Floater raft, Hyperlink mark, _) in swapped)
            {
                row.Inlines.InsertBefore(mark, raft);
                row.Inlines.Remove(mark);
            }

            swapped.Clear();
        }
    }
}
