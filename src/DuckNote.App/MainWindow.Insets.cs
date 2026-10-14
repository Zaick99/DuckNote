using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuckNote.App.Editor;
using DuckNote.App.Views;
using Microsoft.Win32;

namespace DuckNote.App;

public partial class MainWindow
{
    private const string PictureFilter =
        "Immagini|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Tutti i file|*.*";

    private double _textVeil = 0.4;

    private void SetUpInsets()
    {
        InsDraw.Click += (_, _) => Insert(Inset.Fresh(InsetKind.Drawing));
        InsPicture.Click += (_, _) => InsertPicture();
        InsChart.Click += (_, _) => InsertChart();

        DataObject.AddPastingHandler(Editor, OnPaste);

        _textVeil = Editor.SelectionOpacity;
        Editor.SelectionChanged += (_, _) => MarkChosenInsets();
    }

    /// <summary>
    /// Il velo della selezione e' un adorner: sta sopra a tutto, quindi copre
    /// anche un disegno o un grafico. Quando la selezione ne comprende uno il
    /// velo si spegne e sono i riquadri a dire di essere scelti, con un anello.
    /// Sul testo da solo il velo resta quello di sempre.
    /// </summary>
    private void MarkChosenInsets()
    {
        InsetBox[] boxes = InsetFolding.Boxes(Editor.Document);
        if (boxes.Length == 0)
        {
            return;
        }

        bool any = false;

        foreach (InsetBox box in boxes)
        {
            box.Chosen = Within(box);
            any |= box.Chosen;
        }

        Editor.SelectionOpacity = any ? 0 : _textVeil;
    }

    private bool Within(InsetBox box)
    {
        if (Editor.Selection.IsEmpty || box.Raft is not { } raft)
        {
            return false;
        }

        return Editor.Selection.Start.CompareTo(raft.ElementEnd) <= 0
            && Editor.Selection.End.CompareTo(raft.ElementStart) >= 0;
    }

    /// <summary>
    /// Le cornici arrivate dal file non hanno ancora nessuno che le ascolti.
    /// </summary>
    private void AdoptInsets()
    {
        foreach (NotePage page in _book.Pages)
        {
            foreach (InsetBox box in InsetFolding.Boxes(page.Document))
            {
                Adopt(box);
            }
        }
    }

    private void RecolourInsets()
    {
        if (_book is null)
        {
            return;
        }

        foreach (NotePage page in _book.Pages)
        {
            foreach (InsetBox box in InsetFolding.Boxes(page.Document))
            {
                box.Recolour();
            }
        }
    }

    private void Adopt(InsetBox box)
    {
        box.AskForChart = AskForChart;
        box.Changed += Touched;
        box.Removed += Discard;
    }

    private void Touched()
    {
        _noteDirty = true;
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void Discard(InsetBox box)
    {
        if (box.Raft is not { } raft || raft.Parent is not Paragraph row)
        {
            return;
        }

        row.Inlines.Remove(raft);
        Share(row);

        Touched();
        StatusText.Text = $"{box.Inset.Title} tolto dalla nota.";
    }

    private void InsertPicture()
    {
        OpenFileDialog pick = new()
        {
            Title = "Scegli un'immagine",
            Filter = PictureFilter,
            CheckFileExists = true
        };

        if (pick.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Insert(new Inset
            {
                Id = NotePage.FreshId(),
                Kind = InsetKind.Picture,
                Bytes = File.ReadAllBytes(pick.FileName)
            });
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Immagine non letta: il file non e' accessibile.";
        }
    }

    private void InsertChart()
    {
        if (AskForChart(ChartPlan.Example) is not { } written)
        {
            return;
        }

        Insert(new Inset
        {
            Id = NotePage.FreshId(),
            Kind = InsetKind.Chart,
            Bytes = Encoding.UTF8.GetBytes(written)
        });
    }

    private string? AskForChart(string written)
    {
        ChartWindow sheet = new(written, _theme, Resources, _editorBrushes) { Owner = this };

        return sheet.ShowDialog() == true ? sheet.Written : null;
    }

    private void Insert(Inset inset)
    {
        if (Editor.CaretPosition?.Paragraph is not { } caret)
        {
            StatusText.Text = "Metti prima il cursore nel testo.";
            return;
        }

        InsetBox box = new(inset, _editorBrushes);
        Adopt(box);

        _formatter.Suspended = true;
        try
        {
            Paragraph here = Room(caret);
            Floater raft = InsetFolding.Raft(box);

            // Le zattere stanno in testa alla riga, in fila: dopo le parole non
            // le farebbero girare attorno a niente.
            if (here.Inlines.OfType<Floater>().LastOrDefault() is { } last)
            {
                here.Inlines.InsertAfter(last, raft);
            }
            else if (here.Inlines.FirstInline is { } first)
            {
                here.Inlines.InsertBefore(first, raft);
            }
            else
            {
                here.Inlines.Add(raft);
            }

            Share(here);
            Editor.CaretPosition = here.ContentEnd;
        }
        finally
        {
            _formatter.Suspended = false;
        }

        Touched();
        StatusText.Text =
            $"{inset.Title} aggiunto: scrivi di fianco, e trascina i bordi per la misura.";
    }

    /// <summary>
    /// La riga dove mettere un riquadro: quella del cursore se e' libera da
    /// parole, altrimenti una nuova subito sotto.
    ///
    /// Una zattera messa in una riga che ha gia' del testo non lo fa girare
    /// attorno: si sistema sulla riga dopo e le parole restano sopra di lei.
    /// Deve ancorarsi all'inizio di una riga sua; da quel momento tutto quel
    /// che segue -- la stessa riga, le righe nate dall'invio, i paragrafi dopo
    /// -- le gira attorno, e torna a larghezza piena quando il riquadro e'
    /// finito.
    /// </summary>
    private Paragraph Room(Paragraph caret)
    {
        // Una riga che ha gia' un riquadro resta la riga dei riquadri, anche se
        // di fianco ci si e' scritto: il secondo va in fila col primo.
        if (caret.Inlines.OfType<Floater>().Any() || LiveFormatter.TextOf(caret).Trim().Length == 0)
        {
            return caret;
        }

        Paragraph fresh = NoteDocument.Row();

        if (NoteDocument.Siblings(caret) is { } where)
        {
            where.InsertAfter(caret, fresh);
            return fresh;
        }

        return caret;
    }

    /// <summary>
    /// Piu' riquadri nella stessa riga si spartiscono la larghezza: in fila,
    /// stessa misura, allineati. Uno da solo resta della sua, e il testo gli
    /// gira attorno.
    /// </summary>
    private void Share(Paragraph row)
    {
        Floater[] side = [.. row.Inlines.OfType<Floater>()];

        if (side.Length < 2)
        {
            return;
        }

        // Ogni zattera porta il suo stacco a destra, quindi lo stacco va levato
        // dalla larghezza di ognuna, non una volta sola.
        double each = Math.Max(
            Inset.LeastWidth,
            Column() / side.Length - InsetBox.Apart);

        foreach (Floater raft in side)
        {
            raft.Width = each;

            if (raft.Blocks.OfType<BlockUIContainer>().FirstOrDefault()?.Child is InsetBox box)
            {
                box.Inset.Width = Math.Round(each);
            }
        }
    }

    private double Column() => InsetBox.Room(Editor);

    /// <summary>
    /// Un'immagine incollata in un RichTextBox diventa un InlineUIContainer, e
    /// quello non sopravvive al salvataggio: sparirebbe alla riapertura. Qui
    /// l'incollata viene fermata e l'immagine entra come riquadro.
    /// </summary>
    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.Bitmap, autoConvert: true)
            || Png() is not { } bytes)
        {
            return;
        }

        e.CancelCommand();

        Insert(new Inset
        {
            Id = NotePage.FreshId(),
            Kind = InsetKind.Picture,
            Bytes = bytes
        });
    }

    private static byte[]? Png()
    {
        try
        {
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } picture)
            {
                return null;
            }

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(picture));

            using MemoryStream stream = new();
            encoder.Save(stream);

            return stream.ToArray();
        }
        catch (Exception problem) when (problem is System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            return null;
        }
    }
}
