using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Storage;
using DuckNote.App.Theme;
using DuckNote.App.Vault;
using DuckNote.App.Views;

namespace DuckNote.App;

public partial class MainWindow
{
    private readonly EditorBrushes _editorBrushes = new();
    private readonly Dictionary<string, bool> _hostStates = new(StringComparer.OrdinalIgnoreCase);

    private Dresser _renderer = null!;
    private LiveFormatter _formatter = null!;
    private EditorCommands _commands = null!;
    private CodeBlocks _blocks = null!;

    private DispatcherTimer? _formatTimer;
    private DispatcherTimer? _settleTimer;
    private DispatcherTimer? _saveTimer;
    private bool _noteDirty;
    private bool _fresh;
    private bool _tidied;

    private NoteBook _book = null!;
    private NotePage _page = null!;

    private bool _swapping;

    private void SetUpEditor()
    {
        _renderer = new Dresser(_editorBrushes);
        _blocks = new CodeBlocks(Editor, _renderer);
        _formatter = new LiveFormatter(
            Editor,
            new HostPaint(_editorBrushes, () => _hostStates, _session.IsIgnored),
            new CodePaint(_editorBrushes, Editor));
        _commands = new EditorCommands(Editor, _formatter, _renderer, _blocks);
        _commands.Refused += message => StatusText.Text = message;
        _commands.AskForLink = AskForLink;

        Editor.IsDocumentEnabled = true;
        Editor.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnNavigate));

        _editorBrushes.Sync(_theme);
        Editor.Document = NoteDocument.Empty(_editorBrushes.Text);

        Editor.TextChanged += OnEditorTextChanged;
        Editor.PreviewMouseLeftButtonDown += OnEditorClick;
        Editor.PreviewKeyDown += OnEditorKey;

        _formatTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _formatTimer.Tick += (_, _) =>
        {
            _formatTimer!.Stop();
            _formatter.MarkDirty(_formatter.CaretParagraph);
            _formatter.Flush();
            UpdateWordCount();

            SyncNoteHosts();
            RefreshSideList();
        };

        // Si smette di scrivere: quel che era una parola a meta' e' un indirizzo
        // finito, e va trattato come tale.
        _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer!.Stop();
            _formatter.Settle();

            SyncNoteHosts(settled: true);
            RefreshSideList();
        };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer!.Stop();
            SaveNote();
        };

        WireFormatBar();
        SetUpInsets();
        LoadNote();
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatter.IsFormatting || _swapping)
        {
            return;
        }

        _noteDirty = true;
        RestDucks();
        _formatTimer?.Stop();
        _formatTimer?.Start();
        _settleTimer?.Stop();
        _settleTimer?.Start();
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = _blocks.Break() || _commands.NewLine();
    }

    private void OnEditorClick(object sender, MouseButtonEventArgs e)
    {
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: true) is not { } point)
        {
            return;
        }

        if (point.Paragraph is not { } row || !Looks.Is(row, Look.Todo) || !Looks.OnMark(row, point))
        {
            return;
        }

        _formatter.Suspended = true;
        try
        {
            _renderer.Check(row, !Looks.IsDone(row));
        }
        finally
        {
            _formatter.Suspended = false;
        }

        e.Handled = true;
    }

    private LinkChoice? AskForLink(string shown, string address)
    {
        LinkWindow sheet = new(shown, address, _theme, Resources) { Owner = this };

        return sheet.ShowDialog() == true
            ? new LinkChoice(sheet.Address, sheet.Shown, sheet.Removed)
            : null;
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        if (!WebAddress.Safe(e.Uri))
        {
            StatusText.Text = $"Collegamento non aperto: lo schema {e.Uri.Scheme} non e' fra quelli ammessi.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception problem) when (problem is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "Nessun programma risponde per questo collegamento.";
        }
    }

    private void WireFormatBar()
    {
        FmtBold.Click += (_, _) => _commands.Toggle(Mark.Bold);
        FmtItalic.Click += (_, _) => _commands.Toggle(Mark.Italic);
        FmtUnder.Click += (_, _) => _commands.Toggle(Mark.Underline);
        FmtStrike.Click += (_, _) => _commands.Toggle(Mark.Strike);
        FmtMark.Click += (_, _) => _commands.Toggle(Mark.Highlight);
        FmtCode.Click += (_, _) => _commands.Toggle(Mark.Code);
        FmtLink.Click += (_, _) => _commands.Toggle(Mark.Link);

        FmtH1.Click += (_, _) => _commands.SetLook(Look.Heading1);
        FmtH2.Click += (_, _) => _commands.SetLook(Look.Heading2);
        FmtH3.Click += (_, _) => _commands.SetLook(Look.Heading3);
        FmtQuote.Click += (_, _) => _commands.SetLook(Look.Quote);
        FmtBullet.Click += (_, _) => _commands.SetLook(Look.Bullet);
        FmtNumber.Click += (_, _) => _commands.SetLook(Look.Number);
        FmtTodo.Click += (_, _) => _commands.SetLook(Look.Todo);

        FmtClear.Click += (_, _) => _commands.ClearFormatting();
        FmtUndo.Click += (_, _) => Editor.Undo();
        FmtRedo.Click += (_, _) => Editor.Redo();

        FmtCodeBlock.Click += (_, _) => _commands.ToggleCode();
        FmtRule.Click += (_, _) => _commands.InsertRule();
        FmtFind.Click += (_, _) => ToggleFind();

        _tables = new TableCommands(Editor, _formatter, _editorBrushes);
        _tables.Refused += message => StatusText.Text = message;

        TblMenu.Click += (_, _) => DropTableMenu();

        TblNew.Click += (_, _) => _tables.Insert();
        TblRowAdd.Click += (_, _) => _tables.AddRow();
        TblRowDel.Click += (_, _) => _tables.RemoveRow();
        TblColAdd.Click += (_, _) => _tables.AddColumn();
        TblColDel.Click += (_, _) => _tables.RemoveColumn();
    }

    /// <summary>
    /// Il menu della tabella scende da sotto il suo tasto, non dove sta il
    /// mouse: e' un menu di barra, non un menu di destro.
    /// </summary>
    private void DropTableMenu()
    {
        if (TblMenu.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = TblMenu;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private TableCommands? _tables;

    private void RecolourHosts() => _formatter?.RecolourAll();

    /// <summary>
    /// Gli indirizzi scritti nella nota. Quello che il cursore sta ancora
    /// scrivendo non conta: "nb.zaick" non deve comparire in tabella mentre si
    /// va verso "nb.zaick.net".
    /// </summary>
    private IReadOnlyList<string> NoteHosts(bool settled = false)
    {
        List<string> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        Paragraph? writing = settled ? null : _formatter.CaretParagraph;
        int caret = writing is null ? -1 : LiveFormatter.OffsetOf(writing, Editor.CaretPosition);

        foreach (NotePage page in _book.Pages)
        {
            foreach (Paragraph row in LiveFormatter.RowsOf(page.Document))
            {
                bool here = ReferenceEquals(row, writing);

                foreach (HostToken token in HostTokens.Find(LiveFormatter.TextOf(row)))
                {
                    if (here && caret >= 0 && HostTokens.Touches(token, caret))
                    {
                        continue;
                    }

                    if (seen.Add(token.Value) && !_session.IsIgnored(token.Value))
                    {
                        hosts.Add(token.Value);
                    }
                }
            }
        }

        return hosts;
    }

    private void UpdateWordCount()
    {
        string text = NoteDocument.ToPlainText(Editor);
        int words = text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        WordCount.Text = words == 1 ? "1 parola" : $"{words} parole";
    }

    private void LoadNote()
    {
        bool single = false;

        _formatter.Suspended = true;
        try
        {
            byte[]? stored = _vault.IsOpen ? _vault.Note : ReadPlainNote();

            if (_vault.IsDamaged)
            {
                _book = NoteBook.Of(NoteDocument.FromText(Unreadable(), _editorBrushes.Text));
                Editor.IsReadOnly = true;
                StatusText.Text = "Contenitore danneggiato: salvataggio sospeso.";
            }
            else if (stored is { Length: > 0 }
                && NoteBook.Load(stored, _editorBrushes, out single) is { } saved)
            {
                _book = saved;
                StatusText.Text = _vault.IsOpen ? "Nota aperta dalla cassaforte." : "Nota aperta.";
            }
            else
            {
                _book = NoteBook.Of(Welcome(_renderer, _editorBrushes.Text));
                StatusText.Text = "Nota nuova.";

                _fresh = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _book = NoteBook.Of(Welcome(_renderer, _editorBrushes.Text));
            StatusText.Text = "Nota non leggibile: ne apro una nuova.";
        }
        finally
        {
            _formatter.Suspended = false;
        }

        ReadyPages();
        AdoptInsets();
        ShowPage(_book.Pages[0]);

        _noteDirty = _fresh || single || _tidied;

        if (_noteDirty)
        {
            SaveNote();
        }
    }

    private void ReadyPages()
    {
        foreach (NotePage page in _book.Pages)
        {
            _swapping = true;
            try
            {
                Editor.Document = page.Document;
            }
            finally
            {
                _swapping = false;
            }

            _tidied |= NoteDocument.Flatten(page.Document);
            _formatter.PaintAll();
        }
    }

    private void ShowPage(NotePage page)
    {
        _page = page;

        _swapping = true;
        try
        {
            Editor.Document = page.Document;
        }
        finally
        {
            _swapping = false;
        }

        Editor.CaretPosition = page.Document.ContentStart;
        UpdateWordCount();
        RefreshSideList();
    }

    private byte[] NoteXaml() => _book.Save();

    private byte[]? ReadPlainNote()
    {
        try
        {
            return File.Exists(AppPaths.Note) ? File.ReadAllBytes(AppPaths.Note) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Salvare non e' scrivere. Per salvare, le cornici dei riquadri escono dal
    /// documento e tornano: per WPF e' una modifica come un'altra, e contata
    /// come tale rimetteva la nota fra quelle da salvare -- un salvataggio ogni
    /// quattro secondi per sempre -- e faceva ripartire gli orologi che
    /// aspettano che si smetta di scrivere.
    /// </summary>
    private void SaveNote()
    {
        _swapping = true;
        try
        {
            Store();
        }
        finally
        {
            _swapping = false;
        }
    }

    private void Store()
    {
        if (!_noteDirty || Editor.IsReadOnly || _vault.IsDamaged)
        {
            return;
        }

        if (VaultSession.Exists)
        {
            if (!_vault.IsOpen)
            {
                return;
            }

            _vault.RotatePreviousNote();
            _vault.Note = NoteXaml();
            _vault.Save();
            _noteDirty = false;
            return;
        }

        try
        {
            AppPaths.Ensure();

            if (File.Exists(AppPaths.Note))
            {
                File.Copy(AppPaths.Note, AppPaths.NoteBackup, overwrite: true);
            }

            File.WriteAllBytes(AppPaths.Note, NoteXaml());
            _noteDirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Nota non salvata: la cartella non e' scrivibile.";
        }
    }

    private static string Unreadable() =>
        """
        # Il contenitore non si e' lasciato leggere

        La verifica di integrita' non torna: il file e' stato troncato, alterato,
        o scritto da una versione diversa.

        La nota NON viene sovrascritta e il salvataggio resta sospeso, cosi' quel
        che c'e' dentro non va perso.

        Accanto al contenitore c'e' una copia di poco precedente:

            %APPDATA%\DuckNote\store.bin.bak

        Chiudi DuckNote, rinominala in store.bin e riapri.
        """;

    private static FlowDocument Welcome(Dresser dress, Brush ink)
    {
        FlowDocument note = NoteDocument.Empty(ink);

        Add(note, dress, Look.Heading1, "DuckNote");
        Add(note, dress, Look.Text, "Note tecniche e scanner di rete nella stessa finestra.");
        Add(note, dress, Look.Text, string.Empty);
        Add(note, dress, Look.Text,
            "Scrivi un indirizzo e DuckNote lo controlla: il gateway 192.168.1.1, il NAS " +
            "nas.casa.lan, i DNS 1.1.1.1 e 1.0.0.1. Ogni indirizzo riconosciuto prende il " +
            "colore del suo stato: verde raggiungibile, rosso spento, grigio mai provato.");
        Add(note, dress, Look.Text, string.Empty);

        Add(note, dress, Look.Heading2, "Come si scrive");
        Add(note, dress, Look.Text,
            "Si scrive e si preme un tasto della barra: niente marcatori da ricordare. " +
            "Grassetto, corsivo, sottolineato, barrato, evidenziato, codice, titoli, " +
            "elenchi, spunte, citazioni, tabelle, collegamenti.");
        Add(note, dress, Look.Text, string.Empty);

        Add(note, dress, Look.Todo, "provare una spunta, cliccandoci sopra");
        Add(note, dress, Look.Bullet, "una voce di elenco");
        Add(note, dress, Look.Quote, "Una citazione, per le cose dette da altri.");
        Add(note, dress, Look.Text, string.Empty);

        Add(note, dress, Look.Heading2, "Disegni, immagini e grafici");
        Add(note, dress, Look.Text,
            "I tre tasti in fondo alla barra mettono nel testo un riquadro da disegno, " +
            "un'immagine o un grafico. Il riquadro si disegna col mouse; l'immagine arriva " +
            "da un file o dagli appunti; il grafico da qualche numero.");
        Add(note, dress, Look.Text, string.Empty);

        return note;
    }

    private static void Add(FlowDocument note, Dresser dress, Look look, string line)
    {
        Paragraph row = NoteDocument.Row();

        if (line.Length > 0)
        {
            row.Inlines.Add(dress.Plain(line));
        }

        note.Blocks.Add(row);

        if (look != Look.Text)
        {
            dress.Dress(row, look);
        }
    }
}
