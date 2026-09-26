using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Storage;
using DuckNote.App.Vault;

namespace DuckNote.App;

public partial class MainWindow
{
    private readonly EditorBrushes _editorBrushes = new();
    private readonly Dictionary<string, bool> _hostStates = new(StringComparer.OrdinalIgnoreCase);

    private MarkdownRenderer _renderer = null!;
    private LiveFormatter _formatter = null!;
    private EditorCommands _commands = null!;

    private DispatcherTimer? _formatTimer;
    private DispatcherTimer? _saveTimer;
    private bool _noteDirty;
    private bool _fresh;

    private void SetUpEditor()
    {
        _renderer = new MarkdownRenderer(_editorBrushes, () => _hostStates);
        _formatter = new LiveFormatter(Editor, _renderer);
        _commands = new EditorCommands(Editor, _formatter);
        _commands.Refused += message => StatusText.Text = message;

        _editorBrushes.Sync(_theme);
        Editor.Document = NoteDocument.Empty(_editorBrushes.Text);

        Editor.TextChanged += OnEditorTextChanged;
        Editor.PreviewMouseLeftButtonDown += OnEditorClick;

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
        if (_formatter.IsFormatting)
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

    private void OnEditorClick(object sender, MouseButtonEventArgs e)
    {
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: false) is not { } point)
        {
            return;
        }

        if (point.Parent is not Run { Tag: "todo" } || point.Paragraph is not { } paragraph)
        {
            return;
        }

        string text = LiveFormatter.TextOf(paragraph).Replace("\r", string.Empty).Replace("\n", string.Empty);
        ParsedLine parsed = LineParser.Parse(text);
        if (parsed.Kind != LineKind.Todo)
        {
            return;
        }

        string pad = parsed.Indent > 0 ? new string(' ', parsed.Indent) : string.Empty;
        string flipped = pad + (parsed.Done ? "- [ ] " : "- [x] ") + parsed.Body;

        _formatter.Suspended = true;
        try
        {
            new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text = flipped;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            _formatter.Suspended = false;
        }

        paragraph.Tag = null;
        _formatter.Format(paragraph);
        e.Handled = true;
    }

    private void WireFormatBar()
    {
        FmtBold.Click += (_, _) => _commands.Wrap("**");
        FmtItalic.Click += (_, _) => _commands.Wrap("*");
        FmtUnder.Click += (_, _) => _commands.Wrap("__");
        FmtStrike.Click += (_, _) => _commands.Wrap("~~");
        FmtMark.Click += (_, _) => _commands.Wrap("==");
        FmtCode.Click += (_, _) => _commands.Wrap("`");
        FmtLink.Click += (_, _) => _commands.Wrap("[", "]()");

        FmtH1.Click += (_, _) => _commands.SetLinePrefix("# ");
        FmtH2.Click += (_, _) => _commands.SetLinePrefix("## ");
        FmtH3.Click += (_, _) => _commands.SetLinePrefix("### ");
        FmtQuote.Click += (_, _) => _commands.SetLinePrefix("> ");
        FmtBullet.Click += (_, _) => _commands.SetLinePrefix("- ");
        FmtNumber.Click += (_, _) => _commands.SetLinePrefix("1. ");
        FmtTodo.Click += (_, _) => _commands.SetLinePrefix("- [ ] ");

        FmtClear.Click += (_, _) => _commands.ClearFormatting();
        FmtUndo.Click += (_, _) => Editor.Undo();
        FmtRedo.Click += (_, _) => Editor.Redo();
    }

    private void RecolourHosts()
    {
        if (_formatter is null)
        {
            return;
        }

        foreach (Paragraph paragraph in _formatter.AllParagraphs())
        {
            foreach (Inline inline in paragraph.Inlines)
            {
                if (inline is Run { Tag: string tag } run && tag.StartsWith("host:", StringComparison.Ordinal))
                {
                    run.Foreground = _editorBrushes.ForHost(tag[5..], _hostStates);
                }
            }
        }
    }

    private List<(int Level, string Text, Paragraph Paragraph)> EditorOutline()
    {
        List<(int, string, Paragraph)> headings = [];

        foreach (Paragraph paragraph in _formatter.AllParagraphs())
        {
            Match heading = HeadingLine().Match(LiveFormatter.TextOf(paragraph));
            if (heading.Success)
            {
                headings.Add((heading.Groups[1].Value.Length, heading.Groups[2].Value.Trim(), paragraph));
            }
        }

        return headings;
    }

    private IReadOnlyList<string> NoteHosts()
    {
        List<string> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Paragraph paragraph in _formatter.AllParagraphs())
        {
            foreach (HostToken token in HostTokens.Find(LiveFormatter.TextOf(paragraph)))
            {
                if (seen.Add(token.Value))
                {
                    hosts.Add(token.Value);
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
        _formatter.Suspended = true;
        try
        {
            byte[]? stored = _vault.IsOpen ? _vault.Note : ReadPlainNote();

            if (_vault.IsDamaged)
            {
                Editor.Document = NoteDocument.FromText(Unreadable(), _renderer, _editorBrushes.Text);
                Editor.IsReadOnly = true;
                StatusText.Text = "Contenitore danneggiato: salvataggio sospeso.";
            }
            else if (stored is { Length: > 0 }
                && NoteDocument.FromXaml(stored, _editorBrushes.Text) is { } saved)
            {
                Editor.Document = saved;
                StatusText.Text = _vault.IsOpen ? "Nota aperta dalla cassaforte." : "Nota aperta.";
            }
            else
            {
                Editor.Document = NoteDocument.FromText(Welcome(), _renderer, _editorBrushes.Text);
                StatusText.Text = "Nota nuova.";

                _fresh = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Editor.Document = NoteDocument.FromText(Welcome(), _renderer, _editorBrushes.Text);
            StatusText.Text = "Nota non leggibile: ne apro una nuova.";
        }
        finally
        {
            _formatter.Suspended = false;
        }

        _formatter.FormatAll();
        UpdateWordCount();

        _noteDirty = _fresh;
        if (_fresh)
        {
            SaveNote();
        }
    }

    private byte[] NoteXaml() => NoteDocument.ToXaml(Editor);

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
            _vault.Note = NoteDocument.ToXaml(Editor);
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

            File.WriteAllBytes(AppPaths.Note, NoteDocument.ToXaml(Editor));
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
