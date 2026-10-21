using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using DuckNote.App.Editor;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Links;
using DuckNote.Notes.Notebook;
using Microsoft.Win32;

namespace DuckNote.App.Pages;

public partial class NotesPage
{
    private const string PictureFilter = "Immagini|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Tutti i file|*.*";

    private double _veil = 0.4;
    private string? _hostInSight;

    private void WireEditor()
    {
        _veil = Editor.SelectionOpacity;

        Editor.TextChanged += OnTextChanged;
        Editor.PreviewKeyDown += OnEditorKey;
        Editor.PreviewMouseLeftButtonDown += OnEditorClick;
        Editor.PreviewMouseWheel += OnEditorWheel;
        Editor.SelectionChanged += (_, _) => MarkChosenInsets();
        Editor.ContextMenuOpening += (_, _) => DressMenu();
        Editor.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnNavigate));
        DataObject.AddPastingHandler(Editor, OnPaste);

        _settle.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_ws.Settings.HostDebounceMs, 500, 30000));
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_swapping || _watcher.IsPainting || _ws.IsSaving)
        {
            return;
        }

        _ws.Touch();
        _ws.Activity();

        if (Window.GetWindow(this) is MainWindow shell)
        {
            shell.RestDucks();
        }

        _format.Stop();
        _format.Start();
        _settle.Stop();
        _settle.Start();
    }

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        ModifierKeys mods = Keyboard.Modifiers;

        if (RunShortcut(e) || AutoFormat(e))
        {
            e.Handled = true;
            return;
        }

        // Invio: riga nuova senza alcuna formattazione. Maiusc+Invio: riga nuova che la mantiene.
        if (e.Key == Key.Return && mods == ModifierKeys.None)
        {
            e.Handled = _commands.PlainEnter();
            return;
        }

        if (e.Key == Key.Return && mods == ModifierKeys.Shift)
        {
            e.Handled = _commands.KeepEnter();
            return;
        }

        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V)
        {
            PastePlain();
            e.Handled = true;
            return;
        }

        if (mods == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            Zoom(_ws.Settings.EditorZoom + 10);
            e.Handled = true;
            return;
        }

        if (mods == ModifierKeys.Control && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            Zoom(_ws.Settings.EditorZoom - 10);
            e.Handled = true;
            return;
        }

        if (mods == ModifierKeys.Control && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            Zoom(100);
            e.Handled = true;
        }
    }

    /// <summary>Un clic sul quadratino di una spunta la segna fatta (o la riapre).</summary>
    private void OnEditorClick(object sender, MouseButtonEventArgs e)
    {
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: true) is not { Paragraph: { } row } point
            || !Looks.Is(row, Look.Todo) || !Looks.OnMark(row, point))
        {
            return;
        }

        _commands.Change(() => Styler.Check(row, !Looks.IsDone(row)));
        _ws.Touch();
        e.Handled = true;
    }

    /// <summary>Ctrl+rotellina ingrandisce il testo.</summary>
    private void OnEditorWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        Zoom(_ws.Settings.EditorZoom + (e.Delta > 0 ? 10 : -10));
        e.Handled = true;
    }

    private void Zoom(int percent)
    {
        int zoom = Math.Clamp(percent, 50, 300);
        _ws.Settings.EditorZoom = zoom;
        Editor.LayoutTransform = new System.Windows.Media.ScaleTransform(zoom / 100.0, zoom / 100.0);
        ShowCounts();
        _ws.Settings.Save();
    }

    /// <summary>Un collegamento si apre con Ctrl+clic, e solo se e' http, https o mailto.</summary>
    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        if (InsetAddress.Parse(e.Uri) is not null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (!WebAddress.Safe(e.Uri))
        {
            _ws.Say($"Collegamento non aperto: lo schema {e.Uri.Scheme} non e' fra quelli ammessi.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception problem) when (problem is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _ws.Say("Nessun programma risponde per questo collegamento.");
        }
    }

    // ------------------------------------------------------------------ menu sul testo

    /// <summary>Il destro sa cosa sta guardando: su un indirizzo offre analizzarlo, metterlo da parte, copiarlo.</summary>
    private void DressMenu()
    {
        _hostInSight = HostUnderMouse();
        Visibility shown = _hostInSight is null ? Visibility.Collapsed : Visibility.Visible;

        foreach (FrameworkElement item in (FrameworkElement[])[MenuAnalyse, MenuDetails, MenuHide, MenuCopyHost])
        {
            item.Visibility = shown;
        }

        // gli host messi da parte si riprendono sempre da qui, anche senza puntarne uno
        DressHiddenList();
        MenuHostSep.Visibility = _hostInSight is not null || MenuHiddenList.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        // tabella, separazione, blocco di codice o citazione sotto il mouse: si possono eliminare
        _blockInSight = BlockUnderMouse();
        MenuDropBlock.Visibility = MenuBlockSep.Visibility = _blockInSight is null ? Visibility.Collapsed : Visibility.Visible;

        if (_blockInSight is { } block)
        {
            MenuDropBlock.Header = $"Elimina {block.What}";
        }

        if (_hostInSight is not { } host)
        {
            return;
        }

        bool hidden = _ws.Session.IsHidden(host);
        MenuAnalyse.Header = $"Analizza adesso {host}";
        MenuAnalyse.Visibility = MenuDetails.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        MenuDetails.Header = $"Mostra la scheda di {host}";
        MenuHide.Header = hidden ? $"Rimetti in lista {host}" : $"Non analizzare piu' {host}";
        MenuCopyHost.Header = $"Copia {host}";
    }

    private void DressHiddenList()
    {
        MenuHiddenList.Items.Clear();

        foreach (string hidden in _ws.Session.Hidden.Where(other => !string.Equals(other, _hostInSight, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            MenuItem item = new() { Header = $"Rimetti in lista {hidden}" };
            item.Click += (_, _) =>
            {
                _ws.Show(hidden);
                _watcher.PaintAll();
            };
            MenuHiddenList.Items.Add(item);
        }

        MenuHiddenList.Visibility = MenuHiddenList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ blocchi da eliminare

    private sealed record BlockInSight(Block Block, string What);

    private BlockInSight? _blockInSight;

    private BlockInSight? BlockUnderMouse()
    {
        if (Editor.IsReadOnly
            || Editor.GetPositionFromPoint(Mouse.GetPosition(Editor), snapToText: true) is not { Paragraph: { } row })
        {
            return null;
        }

        for (DependencyObject? up = row.Parent; up is not null; up = (up as FrameworkContentElement)?.Parent)
        {
            if (up is Table table)
            {
                return new BlockInSight(table, "la tabella");
            }
        }

        return Looks.Of(row) switch
        {
            Look.Rule => new BlockInSight(row, "la separazione"),
            Look.Code => new BlockInSight(row, "il blocco di codice"),
            Look.Quote => new BlockInSight(row, "la citazione"),
            _ => null
        };
    }

    private void OnMenuDropBlock(object sender, RoutedEventArgs e)
    {
        if (_blockInSight is not { } target || Doc.Siblings(target.Block) is not { } where)
        {
            return;
        }

        _commands.Change(() =>
        {
            Block? next = target.Block.NextBlock ?? target.Block.PreviousBlock;
            where.Remove(target.Block);

            // il foglio non resta mai senza una riga dove scrivere
            if (Editor.Document.Blocks.Count == 0)
            {
                Editor.Document.Blocks.Add(Doc.Row());
            }

            Editor.CaretPosition = (next is { Parent: not null } ? next : Editor.Document.Blocks.FirstBlock).ContentStart;
        });

        _ws.Touch();
        _ws.Say($"{char.ToUpperInvariant(target.What[0])}{target.What[1..]} e' stata eliminata. Ctrl+Z la rimette.");
        _blockInSight = null;
    }

    private string? HostUnderMouse()
    {
        if (!Editor.Selection.IsEmpty && HostTokens.Only(Editor.Selection.Text) is { } picked)
        {
            return picked;
        }

        if (Editor.GetPositionFromPoint(Mouse.GetPosition(Editor), snapToText: true) is not { Paragraph: { } row } point)
        {
            return null;
        }

        try
        {
            return HostTokens.At(Doc.LineOf(row), new TextRange(row.ContentStart, point).Text.Length);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async void OnMenuAnalyse(object sender, RoutedEventArgs e)
    {
        if (_hostInSight is { } host)
        {
            await _ws.Analyse(host);
        }
    }

    private void OnMenuDetails(object sender, RoutedEventArgs e)
    {
        if (_hostInSight is not { } host)
        {
            return;
        }

        if (_ws.Session.IsHidden(host))
        {
            _ws.Say($"{host} e' fra gli host messi da parte: riprendilo per vederne la scheda.");
            return;
        }

        _ws.Session.EnsurePending([host]);
        _ws.RequestHost(host);
    }

    private void OnMenuHide(object sender, RoutedEventArgs e)
    {
        if (_hostInSight is not { } host)
        {
            return;
        }

        if (_ws.Session.IsHidden(host))
        {
            _ws.Show(host);
        }
        else
        {
            _ws.Hide(host);
        }

        _watcher.PaintAll();
    }

    private void OnMenuCopyHost(object sender, RoutedEventArgs e)
    {
        if (_hostInSight is { } host && Copy(host))
        {
            _ws.Say($"{host} copiato.");
        }
    }

    private void OnPastePlain(object sender, RoutedEventArgs e) => PastePlain();

    private void PastePlain()
    {
        try
        {
            if (Clipboard.GetText() is { Length: > 0 } text)
            {
                Editor.Selection.Text = text;
                Editor.CaretPosition = Editor.Selection.End;
            }
        }
        catch (ExternalException)
        {
            _ws.Say("Gli appunti sono occupati da un altro programma: riprova.");
        }
    }

    private bool Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException)
        {
            _ws.Say("Gli appunti sono occupati da un altro programma: riprova.");
            return false;
        }
    }

    // ------------------------------------------------------------------ riquadri

    private void Adopt(FlowDocument document)
    {
        foreach (InsetFrame frame in InsetRafts.Frames(document))
        {
            Adopt(frame);
        }
    }

    private void Adopt(InsetFrame frame)
    {
        frame.AskForChart = text => Views.ChartDialog.Ask(Window.GetWindow(this), text);
        frame.Changed += _ws.Touch;
        frame.Removed += Discard;
    }

    private void OnDrawing(object sender, RoutedEventArgs e) => Insert(Inset.Fresh(InsetKind.Drawing));

    private void OnChart(object sender, RoutedEventArgs e)
    {
        if (Views.ChartDialog.Ask(Window.GetWindow(this), Notes.Charts.ChartSpec.Example) is { } written)
        {
            Insert(Inset.Fresh(InsetKind.Chart, System.Text.Encoding.UTF8.GetBytes(written)));
        }
    }

    private void OnPicture(object sender, RoutedEventArgs e)
    {
        OpenFileDialog pick = new() { Title = "Scegli un'immagine", Filter = PictureFilter, CheckFileExists = true };

        if (pick.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            Insert(Inset.Fresh(InsetKind.Picture, File.ReadAllBytes(pick.FileName)));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            _ws.Say("Immagine non letta: il file non e' accessibile.");
        }
    }

    /// <summary>
    /// Un riquadro va in testa a una riga sua: in una riga che ha gia' del testo la zattera non
    /// farebbe girare le parole attorno. Da li' in poi tutto quel che segue le gira attorno, e
    /// torna a larghezza piena quando il riquadro finisce. Una riga che ha gia' un riquadro
    /// resta la riga dei riquadri: il secondo va in fila col primo.
    /// </summary>
    private void Insert(Inset inset)
    {
        if (Editor.IsReadOnly || Editor.CaretPosition?.Paragraph is not { } caret)
        {
            _ws.Say("Metti prima il cursore nel testo.");
            return;
        }

        InsetFrame frame = new(inset);
        Adopt(frame);

        Paragraph here = caret;

        _commands.Change(() =>
        {
            if (!caret.Inlines.OfType<Floater>().Any() && Doc.LineOf(caret).Trim().Length > 0 && Doc.Siblings(caret) is { } where)
            {
                here = Doc.Row();
                where.InsertAfter(caret, here);
            }

            Floater raft = InsetRafts.Raft(frame);

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

            InsetRafts.Share(here, InsetFrame.Room(Editor));
        });

        Editor.CaretPosition = here.ContentEnd;
        _ws.Touch();
        _ws.Say($"{inset.Title} aggiunto: scrivi di fianco, e trascina i bordi per la misura.");
    }

    private void Discard(InsetFrame frame)
    {
        if (frame.Raft is not { Parent: Paragraph row } raft)
        {
            return;
        }

        _commands.Change(() =>
        {
            row.Inlines.Remove(raft);
            InsetRafts.Share(row, InsetFrame.Room(Editor));
        });

        _ws.Touch();
        _ws.Say($"{frame.Inset.Title} tolto dalla nota.");
    }

    /// <summary>
    /// Un riquadro preso nella selezione si mostra con un anello, e il velo della selezione si
    /// spegne: starebbe sopra al disegno proprio mentre lo si guarda.
    /// </summary>
    private void MarkChosenInsets()
    {
        InsetFrame[] frames = InsetRafts.Frames(Editor.Document);

        if (frames.Length == 0)
        {
            return;
        }

        bool any = false;

        foreach (InsetFrame frame in frames)
        {
            frame.Chosen = !Editor.Selection.IsEmpty && frame.Raft is { } raft
                && Editor.Selection.Start.CompareTo(raft.ElementEnd) <= 0
                && Editor.Selection.End.CompareTo(raft.ElementStart) >= 0;
            any |= frame.Chosen;
        }

        Editor.SelectionOpacity = any ? 0 : _veil;
    }

    /// <summary>Un'immagine incollata diventa un riquadro: incollata cosi' com'e' sparirebbe alla riapertura.</summary>
    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.Bitmap, autoConvert: true) || ClipboardPng() is not { } bytes)
        {
            return;
        }

        e.CancelCommand();
        Insert(Inset.Fresh(InsetKind.Picture, bytes));
    }

    private static byte[]? ClipboardPng()
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
        catch (Exception problem) when (problem is COMException or NotSupportedException or ExternalException)
        {
            return null;
        }
    }
}
