using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Theme;
using DuckNote.App.Ui;

namespace DuckNote.App.Pages;

/// <summary>
/// La nota: la pagina e' il foglio, a tutta finestra; l'indice dei fogli, il pulsante degli host
/// e la barra di formattazione li mette al loro posto la finestra. Qui si collegano il
/// RichTextBox e il motore dell'editor (pittura degli indirizzi e del codice, comandi della
/// barra, tabelle, riquadri, ricerca).
/// </summary>
public partial class NotesPage : UserControl, IDuckPage
{
    private readonly Workspace _ws;
    private readonly RowWatcher _watcher;
    private readonly TextCommands _commands;
    private readonly TableCommands _tables;
    private readonly Finder _finder;

    private readonly DispatcherTimer _format = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _settle = new();

    private Sheet? _sheet;

    /// <summary>Si sta cambiando documento o salvando: per WPF sono modifiche, per la nota no.</summary>
    private bool _swapping;

    public NotesPage(Workspace ws)
    {
        _ws = ws;
        InitializeComponent();

        // indice, comandi e barra vanno nella finestra (vedi il commento nel XAML)
        SideParts.Children.Clear();

        HostPainter hosts = new(ws.StateOf, ws.Session.IsHidden);
        _watcher = new RowWatcher(Editor, hosts, new CodePainter(Editor));
        _commands = new TextCommands(Editor, _watcher) { AskForLink = AskForLink };
        _commands.Refused += ws.Say;
        _tables = new TableCommands(Editor, _commands);
        _tables.Refused += ws.Say;
        _finder = new Finder(Editor);

        ws.Caret = () => _watcher.CaretRow is { } row ? (row, Doc.OffsetOf(row, Editor.CaretPosition)) : (null, -1);
        ws.NoteReplaced += OnNoteReplaced;
        ws.HostsChanged += OnHostsChanged;
        ws.Saved += ShowCounts;
        ThemeManager.Changed += _ => Retint();

        _format.Tick += (_, _) =>
        {
            _format.Stop();
            Typing.Flush(Editor);
            bool settled = _settleNext;
            _settleNext = false;
            _watcher.Flush(settled);
            _ws.SyncHosts(settled);
            RefreshOutline();
            ShowCounts();
        };

        // si smette di scrivere: la parola a meta' e' un indirizzo finito
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            _watcher.Settle();
            _ws.SyncHosts(settled: true);
        };

        WireEditor();
        WirePage();
        BuildToolbar();
        Zoom(ws.Settings.EditorZoom);

        if (ws.Book.Sheets.Count > 0)
        {
            OnNoteReplaced();
        }
    }

    /// <summary>L'indice dei fogli con i loro titoli, da mettere al centro della sidebar.</summary>
    public FrameworkElement Navigator => NavigatorPanel;

    /// <summary>Il pulsante che controlla gli host dei fogli: sta nella scheda del monitoraggio.</summary>
    public FrameworkElement Commands => CommandsPanel;

    /// <summary>La barra di formattazione: sta nella fascia del titolo della finestra, sopra al foglio.</summary>
    public FrameworkElement Toolbar => ToolHost;

    /// <summary>Un clic nell'indice o un foglio nuovo: la finestra deve mostrare questa pagina.</summary>
    public event Action? Wanted;

    /// <summary>Serve l'indice dei fogli (la ricerca da tastiera): se la sidebar e' nascosta deve affacciarsi.</summary>
    public event Action? SidebarWanted;

    /// <summary>
    /// Un'altra pagina (Rete, Mappa, Impostazioni) e' in primo piano: nell'indice nessun foglio
    /// resta evidenziato, nemmeno col pallino, come se fosse aperto.
    /// </summary>
    public void OnHidden()
    {
        _front = false;
        OutlineList.SelectedItem = null;
        RefreshOutline();
    }

    /// <summary>La pagina della nota e' quella in primo piano.</summary>
    private bool _front;

    public void OnShown()
    {
        _front = true;
        RefreshOutline();
        ShowCounts();
        Dispatcher.BeginInvoke(() => Editor.Focus(), DispatcherPriority.Input);
    }

    // ------------------------------------------------------------------ pagine e documento

    private void OnNoteReplaced()
    {
        _swapping = true;

        try
        {
            foreach (Sheet sheet in _ws.Book.Sheets)
            {
                Adopt(sheet.Document);
                _watcher.PaintAll(sheet.Document);
            }
        }
        finally
        {
            _swapping = false;
        }

        _hiddenSeen = string.Join('\n', _ws.Session.Hidden);
        _openBranches.Clear();
        Show(_ws.Book.Sheets.FirstOrDefault());
        Editor.IsReadOnly = _ws.IsDamaged || _ws.IsLocked;
        RefreshTools();
    }

    private void Show(Sheet? sheet)
    {
        if (sheet is null)
        {
            return;
        }

        _sheet = sheet;
        _ws.CurrentSheet = sheet;
        _swapping = true;

        try
        {
            Editor.Document = sheet.Document;
            Editor.CaretPosition = sheet.Document.ContentStart;
            _caretRow = _watcher.CaretRow;
            _undoPaint.Clear();
            _redoPaint.Clear();
        }
        finally
        {
            _swapping = false;
        }

        _finder.Look(string.Empty, false);
        Unmark();
        _anchor = null;
        PlacePage();
        RefreshOutline();
        ShowCounts();
    }

    private string _hiddenSeen = string.Empty;

    /// <summary>
    /// Gli stati sono cambiati: ogni indirizzo prende il suo colore cambiando il pennello, senza
    /// toccare i documenti. Solo se e' cambiato l'elenco degli host messi da parte le righe si
    /// ridipingono (chi e' da parte torna testo normale, chi torna in lista si rivede).
    /// </summary>
    private void OnHostsChanged()
    {
        _watcher.Hosts.Refresh();

        string hidden = string.Join('\n', _ws.Session.Hidden);

        if (hidden != _hiddenSeen)
        {
            _hiddenSeen = hidden;
            RepaintSheets();
        }

        ShowCounts();
    }

    private void RepaintSheets()
    {
        _swapping = true;

        try
        {
            foreach (Sheet sheet in _ws.Book.Sheets)
            {
                _watcher.PaintAll(sheet.Document);
            }
        }
        finally
        {
            _swapping = false;
        }
    }

    private void Retint()
    {
        _swapping = true;

        try
        {
            foreach (Sheet sheet in _ws.Book.Sheets)
            {
                Palette.Retint(sheet.Document);
            }
        }
        finally
        {
            _swapping = false;
        }

        _watcher.Hosts.Refresh();
    }

    private void ShowCounts()
    {
        // Parole e indirizzi si contano rileggendo il foglio: mentre si sta scrivendo si
        // aspetta la pausa (il giro di _format richiama questo metodo), cosi' una nota lunga
        // con un'analisi in corso non rallenta la battitura.
        if (_sheet is null || _format.IsEnabled)
        {
            return;
        }

        if (!TitleBox.IsKeyboardFocusWithin)
        {
            TitleBox.Text = _sheet.Name;
        }

        string text = _sheet.Text;
        int words = text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        int place = _ws.Book.Sheets.ToList().IndexOf(_sheet) + 1;

        List<string> meta =
        [
            words == 1 ? "1 parola" : string.Create(CultureInfo.CurrentCulture, $"{words} parole"),
            string.Create(CultureInfo.CurrentCulture, $"pagina {place} di {_ws.Book.Sheets.Count}")
        ];

        if (_ws.LastSaved is { } saved)
        {
            meta.Add(string.Create(CultureInfo.CurrentCulture, $"salvata alle {saved:HH:mm}"));
        }

        if (_ws.Settings.EditorZoom != 100)
        {
            meta.Add(string.Create(CultureInfo.CurrentCulture, $"zoom {_ws.Settings.EditorZoom}%"));
        }

        if (_ws.Vault.IsOpen)
        {
            meta.Add("cifrata");
        }

        Meta.Text = string.Join("  ·  ", meta);

        bool blank = text.Trim().Length == 0 && !Doc.Deep(_sheet.Document.Blocks).Any(block => block is Table)
            && InsetRafts.Frames(_sheet.Document).Length == 0;
        EmptyHint.Visibility = blank ? Visibility.Visible : Visibility.Collapsed;

        IReadOnlyList<string> hosts = _ws.NoteHosts(settled: true);
        int up = hosts.Count(host => _ws.StateOf(host) == true);
        int down = hosts.Count(host => _ws.StateOf(host) == false);

        HostsChip.Style = (Style)FindResource(hosts.Count == 0 ? "Chip" : down > 0 ? "ChipOrange" : up == hosts.Count ? "ChipGreen" : "ChipBlue");
        HostsChipText.Text = string.Create(CultureInfo.CurrentCulture, $"{up}/{hosts.Count}");
        CommandsPanel.ToolTip = hosts.Count == 0
            ? "Nessun indirizzo scritto nei fogli"
            : string.Create(CultureInfo.CurrentCulture, $"{up} attivi su {hosts.Count} scritti nei fogli: clic per controllarli adesso");
        CommandsPanel.IsEnabled = hosts.Count > 0;
    }

    /// <summary>Il titolo si scrive sul foglio: Invio o un clic altrove lo salva, Esc lo lascia com'era.</summary>
    private void OnTitleKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            Editor.Focus();
        }
        else if (e.Key == System.Windows.Input.Key.Escape && _sheet is not null)
        {
            e.Handled = true;
            TitleBox.Text = _sheet.Name;
            Editor.Focus();
        }
    }

    private void OnTitleDone(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (_sheet is null)
        {
            return;
        }

        string wanted = Notes.Notebook.PageIds.Clean(TitleBox.Text);

        if (wanted == _sheet.Name)
        {
            return;
        }

        _sheet.Name = wanted;
        _ws.Touch();
        _ws.SaveNote();
        RefreshOutline();
        ShowCounts();
    }

    private async void OnScanNote(object sender, RoutedEventArgs e)
    {
        CommandsPanel.IsEnabled = false;

        try
        {
            await _ws.ScanNote();
        }
        finally
        {
            CommandsPanel.IsEnabled = _ws.NoteHosts(settled: true).Count > 0;
        }
    }

    /// <summary>Una tabella di risultati portata nella pagina aperta, sotto la riga del cursore.</summary>
    public void InsertTable(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (_sheet is null || Editor.IsReadOnly)
        {
            return;
        }

        Paragraph here = Editor.CaretPosition?.Paragraph ?? Doc.Rows(Editor.Document).LastOrDefault() ?? Doc.Row();
        Table table = _tables.Build(header, rows);

        _commands.Change(() =>
        {
            if (here.Parent is null)
            {
                Editor.Document.Blocks.Add(here);
            }

            if (Doc.Siblings(here) is { } where)
            {
                where.InsertAfter(here, table);
                where.InsertAfter(table, Doc.Row());
            }
        });

        _watcher.PaintAll();
        _ws.Touch();
        _ws.Say(string.Create(CultureInfo.CurrentCulture, $"Tabella di {rows.Count} host aggiunta alla pagina \"{_sheet.Title}\"."));
    }


    private LinkChoice? AskForLink(string shown, string address) =>
        Views.LinkDialog.Ask(Window.GetWindow(this), shown, address);

}
