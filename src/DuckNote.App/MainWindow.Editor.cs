using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

    private MarkdownRenderer _renderer = null!;
    private LiveFormatter _formatter = null!;
    private EditorCommands _commands = null!;
    private CodeBlocks _blocks = null!;

    private DispatcherTimer? _formatTimer;
    private DispatcherTimer? _saveTimer;
    private bool _noteDirty;
    private bool _fresh;
    private bool _tidied;

    private NoteBook _book = null!;
    private NotePage _page = null!;

    private bool _swapping;

    private void SetUpEditor()
    {
        _renderer = new MarkdownRenderer(_editorBrushes, () => _hostStates);
        _blocks = new CodeBlocks(Editor, _renderer);
        _formatter = new LiveFormatter(Editor, _renderer, new HostPaint(_editorBrushes, () => _hostStates))
        {
            Rules = new InputRules(Editor, _renderer, _blocks, _editorBrushes)
        };
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

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer!.Stop();
            SaveNote();
        };

        WireFormatBar();
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
            ? new LinkChoice(sheet.Address, sheet.Removed)
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

        TblNew.Click += (_, _) => _tables.Insert();
        TblRowAdd.Click += (_, _) => _tables.AddRow();
        TblRowDel.Click += (_, _) => _tables.RemoveRow();
        TblColAdd.Click += (_, _) => _tables.AddColumn();
        TblColDel.Click += (_, _) => _tables.RemoveColumn();
    }

    private TableCommands? _tables;

    private void RecolourHosts() => _formatter?.RecolourAll();

    private IReadOnlyList<string> NoteHosts()
    {
        List<string> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (NotePage page in _book.Pages)
        {
            foreach (Paragraph row in LiveFormatter.RowsOf(page.Document))
            {
                foreach (HostToken token in HostTokens.Find(LiveFormatter.TextOf(row)))
                {
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
                _book = NoteBook.Of(NoteDocument.FromText(Unreadable(), _renderer, _editorBrushes.Text));
                Editor.IsReadOnly = true;
                StatusText.Text = "Contenitore danneggiato: salvataggio sospeso.";
            }
            else if (stored is { Length: > 0 }
                && NoteBook.Load(stored, _editorBrushes.Text, out single) is { } saved)
            {
                _book = saved;
                StatusText.Text = _vault.IsOpen ? "Nota aperta dalla cassaforte." : "Nota aperta.";
            }
            else
            {
                _book = NoteBook.Of(NoteDocument.FromText(Welcome(), _renderer, _editorBrushes.Text));
                StatusText.Text = "Nota nuova.";

                _fresh = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _book = NoteBook.Of(NoteDocument.FromText(Welcome(), _renderer, _editorBrushes.Text));
            StatusText.Text = "Nota non leggibile: ne apro una nuova.";
        }
        finally
        {
            _formatter.Suspended = false;
        }

        bool converted = ConvertPages();
        ShowPage(_book.Pages[0]);

        if (converted)
        {
            StatusText.Text = "Nota convertita: i marcatori sono diventati formattazione.";
        }

        _noteDirty = _fresh || converted || single || _tidied;

        if (_noteDirty)
        {
            SaveNote();
        }
    }

    private bool ConvertPages()
    {
        bool any = false;

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

            if (_formatter.NeedsImport())
            {
                _formatter.Import();
                any = true;
                continue;
            }

            _formatter.PaintAll();
        }

        return any;
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

    private void SaveNote()
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

    [GeneratedRegex(@"^(#{1,3})\s+(.+)$")]
    private static partial Regex HeadingLine();

    private static string Welcome() =>
        """
        # DuckNote

        Note tecniche e scanner di rete nella stessa finestra.

        Scrivi un indirizzo e DuckNote lo controlla: il gateway 192.168.1.1, il NAS
        nas.casa.lan, i DNS 1.1.1.1 e 1.0.0.1. Ogni indirizzo riconosciuto prende il
        colore del suo stato — verde raggiungibile, rosso spento, grigio mai provato.

        ## Come si scrive

        Il testo e' **grassetto**, *corsivo*, __sottolineato__, ~~barrato~~, ==evidenziato==
        e `codice`. I marcatori restano scritti, cosi' il file resta leggibile.

        - [ ] provare una spunta, cliccandoci sopra
        - [x] questa e' gia' fatta
        - un elenco normale

        > Una citazione, per le cose dette da altri.

        ---

        ; le righe che cominciano con punto e virgola sono commenti
        """;
}
