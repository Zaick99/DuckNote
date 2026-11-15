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

        // La selezione resta visibile anche quando il fuoco e' altrove (la casella di ricerca):
        // e' cosi' che si vede la parola trovata. Il grigio di sistema sul foglio non si
        // distingue: si usa l'accento, lo stesso pennello vivo del tema.
        Editor.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = FindResource("Accent");

        Editor.TextChanged += OnTextChanged;
        Editor.PreviewKeyDown += OnEditorKey;
        Editor.PreviewMouseLeftButtonDown += OnEditorClick;
        Editor.PreviewMouseWheel += OnEditorWheel;
        Editor.SelectionChanged += (_, _) =>
        {
            OnCaretMoved();
            MarkChosenInsets();
        };
        Editor.ContextMenuOpening += (_, _) => DressMenu();
        Editor.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnNavigate));
        DataObject.AddPastingHandler(Editor, OnPaste);

        _settle.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_ws.Settings.HostDebounceMs, 500, 30000));
    }

    // ------------------------------------------------------------------ cronologia di Annulla

    /// <summary>
    /// Lo specchio della cronologia dell'editor: per ogni passo, se l'ha fatto chi scrive o la
    /// pittura degli indirizzi. Annulla e Ripeti saltano i passi di pittura, cosi' un Ctrl+Z
    /// toglie quello che si e' scritto e non "il colore dell'indirizzo".
    /// </summary>
    private readonly List<bool> _undoPaint = [];
    private readonly List<bool> _redoPaint = [];
    private bool _undoing;

    private void TrackUndo(UndoAction action)
    {
        switch (action)
        {
            case UndoAction.Create:
                _undoPaint.Add(_watcher.IsPainting);
                _redoPaint.Clear();

                if (_undoPaint.Count > 2000)
                {
                    _undoPaint.RemoveRange(0, 1000);
                }

                break;

            case UndoAction.Undo when _undoPaint.Count > 0:
                _redoPaint.Add(_undoPaint[^1]);
                _undoPaint.RemoveAt(_undoPaint.Count - 1);
                break;

            case UndoAction.Redo when _redoPaint.Count > 0:
                _undoPaint.Add(_redoPaint[^1]);
                _redoPaint.RemoveAt(_redoPaint.Count - 1);
                break;

            case UndoAction.Clear:
                _undoPaint.Clear();
                _redoPaint.Clear();
                break;
        }
    }

    /// <summary>Annulla l'ultima cosa fatta da chi scrive, con la pittura che le stava sopra.</summary>
    private void UndoStep()
    {
        Typing.Flush(Editor);
        _undoing = true;

        try
        {
            while (Editor.CanUndo && _undoPaint.Count > 0 && _undoPaint[^1])
            {
                Editor.Undo();
            }

            Editor.Undo();
        }
        finally
        {
            _undoing = false;
        }
    }

    /// <summary>Ripete l'ultima cosa annullata, e la pittura che la seguiva.</summary>
    private void RedoStep()
    {
        Typing.Flush(Editor);
        _undoing = true;

        try
        {
            Editor.Redo();

            while (Editor.CanRedo && _redoPaint.Count > 0 && _redoPaint[^1])
            {
                Editor.Redo();
            }
        }
        finally
        {
            _undoing = false;
        }
    }

    // ------------------------------------------------------------------ testo che cambia

    private Paragraph? _caretRow;

    /// <summary>Al prossimo giro di pittura le righe si considerano finite (dopo un incolla).</summary>
    private bool _settleNext;

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ReferenceEquals(Editor.Document, _sheet?.Document))
        {
            TrackUndo(e.UndoAction);
        }

        if (_swapping || _watcher.IsPainting || _ws.IsSaving)
        {
            return;
        }

        // il testo e' cambiato: il riquadro della parola trovata non e' piu' al suo posto
        if (_found is not null)
        {
            Unmark();
        }

        _ws.Touch();
        _ws.Activity();

        if (Window.GetWindow(this) is MainWindow shell)
        {
            shell.RestDucks();
        }

        // Si segnano da dipingere le righe toccate da questa modifica: scritte, incollate,
        // spezzate. Annulla e Ripeti rimettono il documento com'era, pittura compresa: li' non
        // si ridipinge, o il nuovo passo cancellerebbe quello che si puo' ancora ripetere.
        if (!_undoing && e.UndoAction is not (UndoAction.Undo or UndoAction.Redo))
        {
            TextPointer start = Editor.Document.ContentStart;

            foreach (TextChange change in e.Changes)
            {
                _watcher.Touch(start.GetPositionAtOffset(change.Offset), start.GetPositionAtOffset(change.Offset + change.AddedLength));
            }
        }

        _format.Stop();
        _format.Start();
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>
    /// Il cursore ha cambiato riga: quella lasciata e' finita, il suo ultimo indirizzo non e'
    /// piu' "una parola che si sta scrivendo" e si colora subito.
    /// </summary>
    private bool _steering;

    private void OnCaretMoved()
    {
        // il cursore non entra nel segno di una voce d'elenco (vedi TextCommands.KeepOutOfMark)
        if (!_steering && !_swapping && !_undoing && !_watcher.IsPainting && !_watcher.Suspended && !Editor.IsReadOnly)
        {
            _steering = true;

            try
            {
                _commands.KeepOutOfMark();
            }
            finally
            {
                _steering = false;
            }
        }

        AirLater();

        Paragraph? now = _watcher.CaretRow;

        if (ReferenceEquals(now, _caretRow))
        {
            return;
        }

        Paragraph? left = _caretRow;
        _caretRow = now;

        if (left?.Parent is null || _swapping || _undoing || Editor.IsReadOnly)
        {
            return;
        }

        _watcher.Touch(left);
        _format.Stop();
        _format.Start();
    }

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        ModifierKeys mods = Keyboard.Modifiers;

        // Invio, Spazio e le scorciatoie lavorano sul documento: prima ci devono essere dentro
        // tutte le lettere gia' battute (vedi Typing), o l'a capo arriva a meta' parola
        if (e.Key is Key.Return or Key.Space or Key.Tab or Key.Back or Key.Delete or Key.Left or Key.Home
            || mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Alt))
        {
            Typing.Flush(Editor);
        }

        if (RunShortcut(e) || AutoFormat(e) || Foreign(e.Key, mods))
        {
            e.Handled = true;
            return;
        }

        // I bordi delle righe con un formato: vedi TextCommands (Backspace, DeleteForward, Left).
        if (e.Key == Key.Back && (mods == ModifierKeys.None || mods == ModifierKeys.Control) && _commands.Backspace())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && mods == ModifierKeys.None)
        {
            _commands.DeleteForward();
        }

        if (e.Key == Key.Left && (mods & ~(ModifierKeys.Shift | ModifierKeys.Control)) == ModifierKeys.None
            && _commands.Left(extend: mods.HasFlag(ModifierKeys.Shift)))
        {
            e.Handled = true;
            return;
        }

        // Invio: riga nuova senza alcuna formattazione. Maiusc+Invio: riga nuova che la mantiene.
        if (e.Key == Key.Return && mods == ModifierKeys.None)
        {
            e.Handled = _commands.RuleFromDashes() || _commands.PlainEnter();
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

    /// <summary>
    /// Le scorciatoie che il RichTextBox ha di suo per formati che DuckNote non usa: apici e
    /// pedici, corpo del carattere, allineamenti, rientri, interlinea, e il tasto Ins che passa
    /// a sovrascrivere. Lasciate fare, mettono nel testo cose che la barra non sa mostrare ne'
    /// togliere, e la formattazione "si blocca". Qui diventano il comando di DuckNote che piu'
    /// somiglia, oppure non fanno niente. Torna true se il tasto e' stato consumato.
    /// </summary>
    private bool Foreign(Key key, ModifierKeys mods)
    {
        const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;

        switch (key)
        {
            // Ctrl e "+" sulle tastiere dove il + sta con Maiusc: ingrandisce, come nei browser
            case Key.OemPlus or Key.Add when mods == CtrlShift:
                Zoom(_ws.Settings.EditorZoom + 10);
                return true;

            // come in Word: Ctrl+Maiusc+L elenco puntato, Ctrl+Maiusc+N testo normale
            case Key.L when mods == CtrlShift:
                if (!Editor.IsReadOnly)
                {
                    _commands.SetLook(Look.Bullet);
                    RefreshTools();
                }

                return true;

            case Key.N when mods == CtrlShift:
                if (!Editor.IsReadOnly)
                {
                    _commands.Plain();
                    RefreshTools();
                }

                return true;

            // sovrascrittura
            case Key.Insert when mods == ModifierKeys.None:
            // allineamento a destra e giustificato, rientro, interlinea
            case Key.R or Key.J or Key.T or Key.D1 or Key.D2 or Key.D5 when mods == ModifierKeys.Control:
            case Key.T when mods == CtrlShift:
            // corpo del carattere
            case Key.OemOpenBrackets or Key.OemCloseBrackets when mods == ModifierKeys.Control:
                return true;

            default:
                return false;
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
        PlacePage();
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
                _settleNext = true;
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
        // Succede a ogni movimento del cursore, quindi a ogni lettera: senza selezione e senza
        // riquadri segnati non c'e' nulla da fare, e non si cercano nemmeno i riquadri.
        if (Editor.Selection.IsEmpty && !_anyChosen)
        {
            return;
        }

        InsetFrame[] frames = _sheet?.Frames ?? [];

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

        _anyChosen = any;
        Editor.SelectionOpacity = any ? 0 : _veil;
    }

    private bool _anyChosen;

    /// <summary>Un'immagine incollata diventa un riquadro: incollata cosi' com'e' sparirebbe alla riapertura.</summary>
    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        // quello che si incolla e' finito: i suoi indirizzi si colorano subito, anche l'ultimo
        _settleNext = true;

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Bitmap, autoConvert: true) || ClipboardPng() is not { } bytes)
        {
            // Testo che arriva da fuori (browser, Word, Excel) porta caratteri, colori e misure
            // suoi: nella nota entra come testo, con lo stile della nota. Quello copiato da
            // DuckNote (ha il formato XAML) tiene titoli, elenchi e segni.
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Xaml) && e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, autoConvert: true))
            {
                e.FormatToApply = DataFormats.UnicodeText;
            }

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
