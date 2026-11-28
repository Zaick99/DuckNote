using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DuckNote.App.Scanning;
using DuckNote.Desktop.Ui;
using DuckNote.Desktop.Views;
using DuckNote.Notes.Groups;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Scanning;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// La rete, come nella versione WPF: comandi in una riga, filtri con i conteggi dentro, la
/// tabella a sinistra e la scheda dell'host scelto a destra. Scorrono solo la tabella e la
/// scheda, ognuna nella sua card.
/// </summary>
public sealed partial class NetworkPage : UserControl, IDuckPage
{
    private static readonly string[] CsvHeader =
        ["Indirizzo", "Stato", "Nome", "NetBIOS", "mDNS", "MAC", "Produttore", "Risposta", "TTL", "Sistema", "Tipo", "Porte aperte", "Servizi", "Titolo web", "Ultima verifica"];

    /// <summary>Il filtro per gruppo: tutti gli host, o quelli fuori dalla nota e dai gruppi.</summary>
    private const string AllGroups = GroupViews.All;
    private const string NoGroup = GroupViews.Found;

    private readonly Workspace _ws;

    /// <summary>
    /// Le righe mostrate: un elenco tutto della tabella, rifatto dai filtri a ogni aggiornamento
    /// (al passo di _refresh), con le stesse righe della sessione.
    /// </summary>
    private readonly ObservableCollection<HostRow> _visible = [];

    // le righe gia' viste e quelle appena trovate da un'analisi in corso (entrano scivolando)
    private readonly HashSet<HostRow> _known = [];
    private readonly HashSet<HostRow> _fresh = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private HostQuery _query = HostQuery.Parse(null);
    private HostRow? _shown;

    private readonly TextBlock _summary = Caption(string.Empty);
    private readonly TextBox _range;
    // un pulsante solo per l'analisi: Analizza, e Ferma mentre e' in corso
    private readonly Button _scan;
    private readonly Glyph _scanIcon = Icon("play", 11);
    private readonly TextBlock _scanText = new() { Text = "Analizza", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _note;
    private readonly Glyph _noteIcon = Icon("check", 11);
    private readonly TextBlock _noteText = new() { Text = "Controlla la nota", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _export;

    private readonly RadioButton _all;
    private readonly RadioButton _up;
    private readonly RadioButton _down;
    private readonly RadioButton _wait;
    private readonly Run _countAll = new("0") { FontWeight = FontWeight.Bold };
    private readonly Run _countUp = new("0") { FontWeight = FontWeight.Bold };
    private readonly Run _countDown = new("0") { FontWeight = FontWeight.Bold };
    private readonly Run _countWait = new("0") { FontWeight = FontWeight.Bold };
    // il filtro di stato scelto: lo tiene questo campo, non i pulsanti (vedi StateSegment)
    private HostState _state = HostState.All;
    private string _group = AllGroups;

    // la scheda dell'host: accanto alla tabella se c'e' posto, altrimenti sopra, a destra, come
    // la scheda di un apparato sulla mappa (e allora c'e' solo quando un host e' scelto)
    private Grid _pageGrid = null!;
    private Border _hostCard = null!;
    private bool _docked = true;
    private readonly TextBox _filter;
    private readonly Button _hidden;
    private readonly TextBlock _hiddenText = new() { Text = "Messi da parte" };

    private readonly DataGrid _table;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyText;
    private readonly TextBlock _footer = Caption(string.Empty);
    private readonly ProgressBar _progress = new() { Width = 220, Maximum = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };

    private readonly StackPanel _cardEmpty;
    private readonly Grid _card;
    private readonly Ellipse _cardDot = new() { Width = 14, Height = 14 };
    private readonly TextBlock _cardTitle;
    private readonly TextBlock _cardSub;
    private readonly Border _cardChip;
    private readonly TextBlock _cardStatus = new() { FontSize = 10.5, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _cardBody = new() { Margin = new Thickness(16, 0, 16, 14) };

    // i comandi della scheda: ricontrollare, aprire (web, condivisioni, terminale), promemoria, altro
    private readonly Button _cardCheck;
    private readonly Button _cardOpen;
    private readonly Button _cardRemind;
    private readonly Button _cardMore;

    /// <summary>"Aggiungi un promemoria" su un host: una voce nuova del calendario legata a lui.</summary>
    public event Action<object>? ReminderWanted;

    /// <summary>"Mostra sulla mappa": l'host com'e' scritto sull'apparato della mappa.</summary>
    public event Action<string>? MapWanted;

    public NetworkPage(Workspace ws)
    {
        _ws = ws;

        // ------------------------------------------------------------ comandi
        _range = new TextBox
        {
            Height = 34,
            MaxWidth = 340,
            FontFamily = Mono,
            FontSize = 12.5,
            PlaceholderText = "192.168.1.0/24  oppure  .1-254",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Text = ws.Settings.LastRange
        };
        ToolTip.SetTip(_range, "CIDR, da-a, .1-254 oppure piu' indirizzi separati da virgole");
        _range.KeyDown += OnRangeKey;

        _scan = Labelled("Primary", _scanIcon, _scanText, "Analizza l'intervallo scritto a sinistra (Invio)");
        _scan.Click += async (_, _) =>
        {
            if (_ws.Session.IsRunning)
            {
                _ws.Session.Stop();
            }
            else
            {
                await OnScanRange();
            }
        };
        _note = Labelled("Soft", _noteIcon, _noteText, "Controlla adesso gli indirizzi scritti nella nota e sulle sue mappe");
        _note.Click += async (_, _) => await _ws.ScanGroup(_ws.Groups.Find(_group)?.Id ?? ScanGroups.Note);

        // portare fuori gli host mostrati: nella nota come tabella, o in un file
        _export = new Button
        {
            Theme = Themes.Of("Quiet"),
            Height = 34,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { Icon("export", 12), new TextBlock { Text = "Esporta", VerticalAlignment = VerticalAlignment.Center }, Icon("down", 9) } }
        };
        ToolTip.SetTip(_export, "Gli host mostrati: come tabella nella nota, o in un file CSV");
        _export.Click += (_, _) => OnExportMenu();

        TextBlock title = new() { Text = "Rete", FontSize = 21, FontWeight = FontWeight.Bold };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("Label"));

        Grid commands = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        commands.Children.Add(Art.Picture("art/network.png", 44));
        commands.Children.Add(Cell(new StackPanel { Margin = new Thickness(12, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center, Children = { title, _summary } }, 1));
        commands.Children.Add(Cell(_range, 2));
        commands.Children.Add(Cell(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _scan, _note }
        }, 3));
        commands.Children.Add(Cell(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Separator(), _export }
        }, 4));

        // ------------------------------------------------------------ filtri
        _all = StateSegment(HostState.All, Counted("Tutti ", _countAll), true);
        _up = StateSegment(HostState.Up, Counted("Attivi ", _countUp));
        _down = StateSegment(HostState.Down, Counted("Spenti ", _countDown));
        _wait = StateSegment(HostState.Wait, Counted("Da controllare ", _countWait));
        _filter = new TextBox
        {
            Height = 32,
            Width = 280,
            PlaceholderText = "Cerca: testo, ip: nome: porta: os:",
            VerticalAlignment = VerticalAlignment.Center,
            InnerLeftContent = new Glyph { Kind = "search", Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), Opacity = 0.6 }
        };
        ToolTip.SetTip(_filter, "Testo libero, oppure per campo: ip: nome: mac: porta: os: tipo: produttore: gruppo: web: nota: stato:");
        _filter.TextChanged += (_, _) => OnFilter();

        _hidden = new Button
        {
            Theme = Themes.Of("Quiet"),
            Height = 32,
            Margin = new Thickness(10, 0, 0, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icon("hide", 10), _hiddenText } }
        };
        ToolTip.SetTip(_hidden, "Gli host che non entrano in nessuna scansione");
        _hidden.Click += (_, _) => OnHiddenMenu();

        // la ricerca si stringe quando la pagina e' stretta: i filtri e "Messi da parte" restano interi
        _filter.Width = double.NaN;
        _filter.MaxWidth = 280;
        _filter.MinWidth = 120;
        _filter.HorizontalAlignment = HorizontalAlignment.Right;
        Grid filters = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        filters.Children.Add(Well(_all, _up, _down, _wait));
        filters.Children.Add(Cell(_filter, 1, new Thickness(10, 0, 0, 0)));
        filters.Children.Add(Cell(_hidden, 2));

        // ------------------------------------------------------------ tabella
        _table = HostTable();
        _table.ItemsSource = _visible;
        _table.LoadingRow += OnLoadingRow;
        _table.SelectionChanged += (_, _) => Show(_table.SelectedItem as HostRow);
        _table.AddHandler(PointerPressedEvent, OnTablePress, RoutingStrategies.Tunnel);
        _table.ContextMenu = RowMenu();

        _emptyTitle = new TextBlock { Text = "Nessun host da mostrare", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        _emptyTitle.Classes.Add("sub");
        _emptyText = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        _emptyText.Classes.Add("muted");
        _empty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 380,
            IsHitTestVisible = false,
            Children = { Art.Picture("art/empty_hosts.png", 120), _emptyTitle, _emptyText }
        };

        Border footer = new()
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 8),
            Child = new Grid { Children = { Trimmed(_footer, new Thickness(0, 0, 240, 0)), _progress } }
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("SeparatorSoft"));

        Grid tableArea = new() { RowDefinitions = new RowDefinitions("*,Auto") };
        tableArea.Children.Add(_table);
        tableArea.Children.Add(_empty);
        tableArea.Children.Add(Row(footer, 1));

        Border tableCard = new() { Child = tableArea, ClipToBounds = true };
        tableCard.Classes.Add("card");

        // ------------------------------------------------------------ scheda dell'host
        TextBlock pick = new() { Text = "Scegli un host", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        pick.Classes.Add("sub");
        TextBlock pickText = new()
        {
            Text = "Qui compare tutto quello che la scansione ha raccolto: nomi, MAC e produttore, servizi, certificato, SNMP.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        pickText.Classes.Add("muted");
        _cardEmpty = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24),
            Children = { Art.Picture("art/hosts.png", 96), pick, pickText }
        };

        _cardTitle = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        _cardTitle.Classes.Add("sub");
        _cardSub = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = Mono };
        _cardSub.Classes.Add("caption");
        _cardChip = new Border { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 1), CornerRadius = new CornerRadius(9), Child = _cardStatus };

        Border dotWell = new() { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), VerticalAlignment = VerticalAlignment.Center, Child = _cardDot };
        dotWell.Bind(Border.BackgroundProperty, dotWell.GetResourceObservable("BgFieldAlt"));

        Button close = new() { Theme = Themes.Of("Tool"), Width = 26, Height = 26, MinWidth = 26, Content = Icon("close", 11), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(close, "Chiudi la scheda (Esc)");
        close.Click += (_, _) => CloseCard();

        Grid head = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        head.Children.Add(dotWell);
        head.Children.Add(Cell(new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _cardTitle, _cardSub, _cardChip } }, 1));
        head.Children.Add(Cell(close, 2));

        _cardCheck = CardButton("refresh", "Ricontrolla", "Analizza di nuovo questo host, per intero");
        _cardCheck.Click += async (_, _) =>
        {
            if (_shown is { } row)
            {
                await _ws.Analyse(Target(row));
            }
        };
        _cardOpen = CardButton("open", "Apri", "Le pagine web, le condivisioni e il terminale di questo host", chevron: true);
        _cardOpen.Click += (_, _) => OnOpenMenu(_cardOpen);
        _cardRemind = CardButton("bell", null, "Aggiungi un promemoria su questo host");
        _cardRemind.Click += (_, _) =>
        {
            if (_shown is { } row)
            {
                ReminderWanted?.Invoke(Reminder(row));
            }
        };
        _cardMore = CardButton("more", null, "Altro: copia l'indirizzo, mostralo sulla mappa, mettilo da parte");
        _cardMore.Click += (_, _) => OnMoreMenu(_cardMore);

        Grid actions = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(_cardCheck);
        actions.Children.Add(Cell(_cardOpen, 1, new Thickness(6, 0, 0, 0)));
        actions.Children.Add(Cell(_cardRemind, 3));
        actions.Children.Add(Cell(_cardMore, 4, new Thickness(6, 0, 0, 0)));

        Border headBar = new() { Padding = new Thickness(16, 14), BorderThickness = new Thickness(0, 0, 0, 1), Child = new StackPanel { Children = { head, actions } } };
        headBar.Bind(Border.BorderBrushProperty, headBar.GetResourceObservable("SeparatorSoft"));

        _card = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), IsVisible = false };
        _card.Children.Add(headBar);
        _card.Children.Add(Row(new ScrollViewer { Content = _cardBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 1));

        _hostCard = new Border { Child = new Grid { Children = { _cardEmpty, _card } }, ClipToBounds = true };
        _hostCard.Classes.Add("card");

        // ------------------------------------------------------------ pagina
        // a sinistra i gruppi, al centro la tabella, a destra la scheda dell'host
        Grid page = new()
        {
            Margin = new Thickness(20, 14, 20, 14),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("250,14,*,14,340")
        };
        _pageGrid = page;
        page.Children.Add(Span(commands, 0));
        Control groupsPanel = GroupsPanel();
        Grid.SetRow(groupsPanel, 1);
        Grid.SetRowSpan(groupsPanel, 2);
        page.Children.Add(groupsPanel);
        Grid.SetRow(filters, 1);
        Grid.SetColumn(filters, 2);
        Grid.SetColumnSpan(filters, 3);
        page.Children.Add(filters);
        Grid.SetRow(tableCard, 2);
        Grid.SetColumn(tableCard, 2);
        page.Children.Add(tableCard);
        Grid.SetRow(_hostCard, 2);
        Grid.SetColumn(_hostCard, 4);
        page.Children.Add(_hostCard);
        Content = page;
        page.SizeChanged += (_, e) => Dock(e.NewSize.Width);

        // durante una scansione arrivano decine di esiti al secondo: la tabella si rinfresca a
        // passo d'uomo, non a ogni risultato
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            ShowSession();
        };

        ws.Session.PropertyChanged += (_, _) => Later();
        ws.HostsChanged += Later;
        ws.GroupsChanged += OnGroupsChanged;
        ShowGroup();
        ShowSession(force: true);
    }

    private void OnGroupsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnGroupsChanged);
            return;
        }

        ShowGroup();
        Later();
    }

    private void Later()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Later);
            return;
        }

        if (!_refresh.IsEnabled)
        {
            _refresh.Start();
        }
    }

    public void OnShown()
    {
        // le righe che ci sono adesso entrano in cascata; quelle che arriveranno, una per volta
        _known.Clear();
        _known.UnionWith(_ws.Session.Rows);
        _fresh.Clear();
        ShowSession(force: true);
        _range.Focus();
        Dispatcher.UIThread.Post(() => Motion.Cascade(
            _table.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.IsVisible).OrderBy(row => row.Bounds.Top).Cast<Control>(),
            start: 60), DispatcherPriority.Background);
    }

    /// <summary>Una riga che il DataGrid prepara: se e' un host appena trovato, entra scivolando.</summary>
    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is HostRow row && _fresh.Remove(row))
        {
            Motion.Arrive(e.Row);
        }
    }

    public void OnHidden()
    {
    }

    /// <summary>Apre la scheda di un host (chiesta dalla nota o dall'elenco host).</summary>
    public void Reveal(string host)
    {
        if (_ws.Session.Find(host) is not { } row)
        {
            _ws.Say($"Di {host} non c'e' ancora nulla da mostrare.");
            return;
        }

        if (!_visible.Contains(row))
        {
            _filter.Text = string.Empty;
            _group = AllGroups;
            ShowGroup();
            _all.IsChecked = true;
            _query = HostQuery.Parse(null);
            Fill();
        }

        _table.SelectedItem = row;
        _table.ScrollIntoView(row, null);
        Show(row);
    }

    private bool Wanted(HostRow row) => GroupViews.Matches(_state, row) && InGroup(row) && _query.Matches(row.Facts());

    private bool InGroup(HostRow row) => GroupViews.InGroup(_group, row, _ws.Groups);

    /// <summary>
    /// Allinea le righe mostrate ai filtri toccando solo quelle che cambiano: via chi non c'entra
    /// piu', dentro i nuovi al loro posto. Se l'ordine e' cambiato l'elenco si rifa' da capo,
    /// tenendo la riga scelta.
    /// </summary>
    private void Fill()
    {
        List<HostRow> wanted = [.. _ws.Session.Rows.Where(Wanted)];
        HashSet<HostRow> keep = [.. wanted];

        // durante un'analisi gli host appena trovati entrano scivolando (OnLoadingRow)
        bool scanning = _ws.Session.IsRunning && IsVisible;

        foreach (HostRow row in wanted)
        {
            if (_known.Add(row) && scanning)
            {
                _fresh.Add(row);
            }
        }

        for (int i = _visible.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_visible[i]))
            {
                _visible.RemoveAt(i);
            }
        }

        HashSet<HostRow> shown = [.. _visible];

        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < _visible.Count && ReferenceEquals(_visible[i], wanted[i]))
            {
                continue;
            }

            if (shown.Contains(wanted[i]))
            {
                HostRow? chosen = _table.SelectedItem as HostRow;
                _visible.Clear();

                foreach (HostRow row in wanted)
                {
                    _visible.Add(row);
                }

                if (chosen is not null && keep.Contains(chosen))
                {
                    _table.SelectedItem = chosen;
                }

                return;
            }

            _visible.Insert(i, wanted[i]);
            shown.Add(wanted[i]);
        }
    }

    private void ShowSession(bool force = false)
    {
        // una pagina che non si vede non si aggiorna: lo fa OnShown quando torna in primo piano
        if (!force && !IsVisible)
        {
            return;
        }

        ScanSession session = _ws.Session;
        CultureInfo culture = CultureInfo.CurrentCulture;

        // i numeri dei filtri contano dentro il gruppo scelto e la ricerca: sono le righe che mostrano
        (int all, int up, int down, int wait) = GroupViews.Count(session.Rows.Where(row => InGroup(row) && _query.Matches(row.Facts())));
        _countAll.Text = all.ToString(culture);
        _countUp.Text = up.ToString(culture);
        _countDown.Text = down.ToString(culture);
        _countWait.Text = wait.ToString(culture);
        _hiddenText.Text = session.Hidden.Count == 0 ? "Messi da parte" : string.Create(culture, $"Messi da parte ({session.Hidden.Count})");

        int fromNote = session.Rows.Count(row => row.FromNote);
        _summary.Text = session.Rows.Count == 0
            ? "Nessun host ancora"
            : string.Create(culture, $"{session.Rows.Count} host, {fromNote} dalla nota · {session.UpCount} attivi");

        bool running = session.IsRunning;
        _scanIcon.Kind = running ? "stop" : "play";
        _scanText.Text = running ? "Ferma" : "Analizza";
        ToolTip.SetTip(_scan, running ? "Ferma l'analisi in corso" : "Analizza l'intervallo scritto a sinistra (Invio)");
        _note.IsEnabled = !running;
        _range.IsEnabled = !running;
        _export.IsEnabled = _visible.Count > 0 || session.Rows.Count > 0;
        _progress.IsVisible = session.IsRunning;
        _progress.Value = session.Progress;

        Fill();
        FillGroups();

        int shown = _visible.Count;
        _empty.IsVisible = shown == 0;
        _emptyTitle.Text = session.Rows.Count == 0 ? "Nessun host da mostrare" : "Nessun host corrisponde ai filtri";
        _emptyText.Text = session.Rows.Count == 0
            ? "Scrivi un intervallo e premi Analizza, oppure scrivi degli indirizzi in una pagina della nota."
            : "Cambia filtro o ricerca per vederne altri.";

        _footer.Text = session.IsRunning
            ? session.Message
            : string.Create(culture, $"{shown} di {session.Rows.Count} mostrati · {session.Message}");

        if (_shown is not null && !session.Rows.Contains(_shown))
        {
            Show(null);
        }
        else if (_shown is not null)
        {
            FillCard(_shown);
        }
    }

    private async Task OnScanRange()
    {
        string range = _range.Text?.Trim() ?? string.Empty;

        if (range.Length == 0)
        {
            Shake(_range);
            _ws.Say("Indica un intervallo, per esempio 192.168.1.0/24");
            return;
        }

        await _ws.ScanRange(range);
    }

    private async void OnRangeKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_ws.Session.IsRunning)
        {
            e.Handled = true;
            await OnScanRange();
        }
    }

    private void OnFilter()
    {
        _query = HostQuery.Parse(_filter.Text);
        ShowSession();
    }

    // ------------------------------------------------------------------ scheda

    private void Show(HostRow? row)
    {
        bool fresh = !ReferenceEquals(row, _shown);
        _shown = row;
        _cardEmpty.IsVisible = row is null;
        _card.IsVisible = row is not null;
        // sopra la tabella la scheda c'e' solo con un host scelto
        _hostCard.IsVisible = _docked || row is not null;

        if (row is not null)
        {
            FillCard(row);

            if (fresh)
            {
                Motion.Show(_card, rise: 8, ms: 220);
            }
        }
    }

    private void FillCard(HostRow row)
    {
        _cardCheck.IsEnabled = !_ws.Session.IsRunning && !_ws.IsLocked;
        _cardOpen.IsVisible = Doors(row).Count > 0;
        _cardRemind.IsEnabled = !_ws.IsLocked;

        _cardTitle.Text = row.Name.Length > 0 ? row.Name : row.IP;
        // l'IP sotto al titolo solo se il titolo e' un nome: due volte lo stesso numero non serve
        _cardSub.Text = string.Join("  ·  ", ((string[])[row.Name.Length > 0 ? row.IP : string.Empty, row.DeviceType]).Where(part => part.Length > 0));
        _cardSub.IsVisible = _cardSub.Text.Length > 0;
        _cardStatus.Text = row.Status.ToUpperInvariant();
        _cardDot.Bind(Shape.FillProperty, _cardDot.GetResourceObservable(row.Tone));
        _cardStatus.Bind(TextBlock.ForegroundProperty, _cardStatus.GetResourceObservable(row.Tone));
        _cardChip.Bind(Border.BackgroundProperty, _cardChip.GetResourceObservable(row.Tone + "Soft"));

        _cardBody.Children.Clear();

        Section("Identita'",
            ("Indirizzo IP", row.IP), ("Nella nota come", row.NoteKey == row.IP ? string.Empty : row.NoteKey),
            ("Nome DNS", row.Hostname), ("Nome NetBIOS", row.NetBiosName), ("Nome mDNS", row.MdnsName),
            ("Gruppo/Dominio", First(row.Workgroup, row.Domain)), ("Indirizzo MAC", row.Mac), ("Produttore", row.Vendor),
            ("Utente", row.LoggedUser));
        Section("Raggiungibilita'",
            ("Stato", row.Status), ("Latenza", row.RoundTrip), ("Perdita", row.Loss), ("TTL", row.Ttl),
            ("Ultima volta attivo", row.LastSeen), ("Durata analisi", row.ScanMs > 0 ? $"{row.ScanMs} ms" : string.Empty));
        Section("Sistema",
            ("Stima OS", row.Os), ("Tipo", row.DeviceType), ("Modello", First(row.Model, row.WmiModel)), ("Riconosciuto da", row.Evidence),
            ("Sistema (WMI)", row.WmiOs),
            ("Seriale", row.WmiSerial), ("CPU", row.WmiCpu), ("Memoria", row.WmiRam), ("Dischi", row.WmiDisks), ("Uptime", row.WmiUptime));
        Section("Servizi",
            ("Porte aperte", row.OpenPorts), ("Servizi", row.Services), ("Servizi mDNS", row.MdnsServices), ("Condivisioni", row.Shares), ("RDP", row.RdpInfo));
        Section("Applicazioni",
            ("Titolo web", row.HttpTitle), ("Server HTTP", row.HttpServer), ("SSH", row.SshBanner), ("FTP", row.FtpBanner),
            ("SMTP", row.SmtpBanner), ("UPnP", string.Join(" / ", ((string[])[row.UpnpDevice, row.UpnpServer]).Where(p => p.Length > 0))));
        Section("Certificato TLS", ("Soggetto", row.TlsSubject), ("Emittente", row.TlsIssuer), ("Scadenza", row.TlsExpiry));
        Section("SNMP",
            ("Nome", row.SnmpName), ("Descrizione", row.SnmpDescr), ("Posizione", row.SnmpLocation),
            ("Contatto", row.SnmpContact), ("Uptime", row.SnmpUptime));

        if (row.Findings.Length > 0)
        {
            SelectableTextBlock text = new() { Text = row.Findings, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Label"));
            Border note = new() { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12, 10), Child = text };
            note.Classes.Add("well");
            _cardBody.Children.Add(note);
        }

        if (!row.IsChecked)
        {
            TextBlock wait = new() { Text = "Non ancora analizzato: il controllo parte da solo fra poco.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
            wait.Classes.Add("muted");
            _cardBody.Children.Add(wait);
        }
    }

    private void Section(string title, params (string Label, string? Value)[] lines)
    {
        (string Label, string Value)[] shown = [.. lines.Where(line => !string.IsNullOrWhiteSpace(line.Value)).Select(line => (line.Label, line.Value!))];

        if (shown.Length == 0)
        {
            return;
        }

        TextBlock header = new() { Text = title.ToUpperInvariant(), Margin = new Thickness(0, 16, 0, 6) };
        header.Classes.Add("section");
        header.Margin = new Thickness(0, 16, 0, 6);
        _cardBody.Children.Add(header);

        foreach ((string label, string value) in shown)
        {
            Grid line = new() { Margin = new Thickness(0, 0, 0, 5), ColumnDefinitions = new ColumnDefinitions("110,*") };

            TextBlock name = new() { Text = label, TextWrapping = TextWrapping.Wrap };
            name.Classes.Add("caption");
            SelectableTextBlock content = new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
            content.Bind(TextBlock.ForegroundProperty, content.GetResourceObservable("Label"));

            line.Children.Add(name);
            line.Children.Add(Cell(content, 1));
            _cardBody.Children.Add(line);
        }
    }

    private static string First(params string[] candidates) => candidates.FirstOrDefault(c => c.Length > 0) ?? string.Empty;

    // ------------------------------------------------------------------ menu, esportazione

    private HostRow? Chosen => _table.SelectedItem as HostRow;

    /// <summary>Il tasto destro sceglie la riga sotto al puntatore, come in Esplora risorse.</summary>
    private void OnTablePress(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_table).Properties.IsRightButtonPressed)
        {
            return;
        }

        for (Visual? at = e.Source as Visual; at is not null && !ReferenceEquals(at, _table); at = at.GetVisualParent())
        {
            if (at is DataGridRow { DataContext: HostRow row })
            {
                _table.SelectedItem = row;
                return;
            }
        }
    }

    private ContextMenu RowMenu()
    {
        ContextMenu menu = new();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            HostRow? row = Chosen;
            bool any = row is not null;

            menu.Items.Add(MenuEntry("Analizza di nuovo", "refresh", "Green", any && !_ws.Session.IsRunning, async () =>
            {
                if (Chosen is { } chosen)
                {
                    await _ws.Analyse(chosen.NoteKey.Length > 0 ? chosen.NoteKey : chosen.IP);
                }
            }));
            if (row is not null)
            {
                foreach (HostDoor door in Doors(row))
                {
                    menu.Items.Add(MenuEntry($"Apri {door.Label}", DoorIcon(door), "Blue", true, () => Open(door)));
                }

                if (_ws.MapHostOf(row) is { } onMap)
                {
                    menu.Items.Add(MenuEntry("Mostra sulla mappa", "map", "Teal", true, () =>
                    {
                        MapWanted?.Invoke(onMap);
                        return Task.CompletedTask;
                    }));
                }
            }

            menu.Items.Add(MenuEntry("Copia indirizzo", "copy", null, any, async () =>
            {
                if (Chosen is { } chosen)
                {
                    await CopyAddress(chosen);
                }
            }));
            menu.Items.Add(MenuEntry("Aggiungi un promemoria…", "bell", "Orange", any && !_ws.IsLocked, () =>
            {
                if (Chosen is { } chosen)
                {
                    ReminderWanted?.Invoke(Reminder(chosen));
                }

                return Task.CompletedTask;
            }));
            if (row is not null)
            {
                menu.Items.Add(GroupsMenu(row));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry(row is null ? "Metti da parte" : $"Metti da parte {row.Shown}", "hide", "Red", any, () =>
            {
                if (Chosen is { } chosen)
                {
                    _ws.Hide(chosen.NoteKey.Length > 0 ? chosen.NoteKey : chosen.IP);
                }

                return Task.CompletedTask;
            }));

            // chi e' stato messo da parte non e' piu' in tabella: si riprende da qui, col tasto destro
            if (_ws.Session.Hidden.Count > 0)
            {
                MenuItem list = new() { Header = "Host messi da parte", Icon = MenuIcon("show", "Green") };

                foreach (string host in _ws.Session.Hidden.ToList())
                {
                    MenuItem item = new() { Header = $"Rimetti in lista {host}" };
                    item.Click += (_, _) => _ws.Show(host);
                    list.Items.Add(item);
                }

                menu.Items.Add(list);
            }
        };

        return menu;
    }

    /// <summary>Il menu degli host messi da parte: uno per riga, un clic li riprende.</summary>
    private void OnHiddenMenu()
    {
        ContextMenu menu = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            PlacementTarget = _hidden
        };

        if (_ws.Session.Hidden.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Nessuno. Si mette da parte col tasto destro, nella nota o qui.", IsEnabled = false });
        }

        foreach (string host in _ws.Session.Hidden.ToList())
        {
            MenuItem item = new() { Header = $"Riprendi {host}" };
            ToolTip.SetTip(item, "Torna ad analizzarlo");
            item.Click += (_, _) => _ws.Show(host);
            menu.Items.Add(item);
        }

        menu.Open(_hidden);
    }

    /// <summary>
    /// La voce del calendario per un host: legata a lui; se ha un certificato che scade, gia'
    /// pronta come scadenza una settimana prima.
    /// </summary>
    // ------------------------------------------------------------------ i gruppi di un host

    /// <summary>
    /// "Gruppi" per un host: aggiungerlo a un gruppo (una riga in fondo), toglierlo da quelli che
    /// lo nominano, o farne uno nuovo che parte da lui. Chi ci sta per una rete resta li'.
    /// </summary>
    private MenuItem GroupsMenu(HostRow row)
    {
        MenuItem menu = new() { Header = "Gruppi", Icon = MenuIcon("group", "Accent"), IsEnabled = !_ws.IsLocked };
        GroupHosts groups = _ws.Groups;
        string[] names = [.. row.Names()];

        foreach (ScanGroup group in groups.All)
        {
            string name = Short(group.Name, 34);

            if (!groups.Contains(group, row))
            {
                menu.Items.Add(MenuEntry($"Aggiungi a «{name}»", "add", "Green", true, () =>
                {
                    ChangeGroup(ScanGroups.WithTarget(group, Target(row)), $"{row.Shown} aggiunto a «{group.Name}».");
                    return Task.CompletedTask;
                }));
            }
            else if (ScanGroups.Listed(group, names).Count > 0)
            {
                menu.Items.Add(MenuEntry($"Togli da «{name}»", "close", "Red", true, () =>
                {
                    ChangeGroup(ScanGroups.WithoutHost(group, names), $"{row.Shown} tolto da «{group.Name}».");
                    return Task.CompletedTask;
                }));
            }
            else
            {
                MenuItem covered = new() { Header = $"In «{name}» per la sua rete", IsEnabled = false, Icon = MenuIcon("check", null) };
                menu.Items.Add(covered);
            }
        }

        if (groups.All.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(MenuEntry("Nuovo gruppo con questo host…", "add", "Accent", true, () => EditGroup(null, Target(row))));
        return menu;
    }

    /// <summary>Salva un gruppo cambiato al volo (un host aggiunto o tolto).</summary>
    private void ChangeGroup(ScanGroup next, string message)
    {
        List<ScanGroup> all = [.. _ws.Groups.All];
        int at = all.FindIndex(group => group.Id == next.Id);

        if (at < 0)
        {
            return;
        }

        all[at] = next;
        _ws.SaveGroups(all);
        _ws.Say(message);
        ShowGroup();
        ShowSession();
    }

    // ------------------------------------------------------------------ comandi della scheda

    /// <summary>Come si chiama l'host per i comandi: com'e' scritto nella nota, o il suo IP.</summary>
    private static string Target(HostRow row) => row.NoteKey.Length > 0 ? row.NoteKey : row.IP;

    /// <summary>Quello che si apre su questo host, dalle sue porte aperte (sul Mac: Finder, Terminale, browser).</summary>
    private static IReadOnlyList<HostDoor> Doors(HostRow row) => HostDoors.For(row.IP.Length > 0 ? row.IP : row.NoteKey, row.OpenPorts, mac: true);

    private static string DoorIcon(HostDoor door) => door.Kind switch
    {
        "web" => "web",
        "share" => "folder",
        "ssh" => "code",
        _ => "open"
    };

    private async Task Open(HostDoor door)
    {
        bool opened = false;

        try
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            {
                opened = await launcher.LaunchUriAsync(new Uri(door.Target));
            }
        }
        catch (Exception problem) when (problem is UriFormatException or InvalidOperationException)
        {
            opened = false;
        }

        _ws.Say(opened ? $"Aperto {door.Label}." : $"Non si e' aperto {door.Label}: nessuna app lo sa aprire.");
    }

    private async Task CopyAddress(HostRow row)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(row.IP.Length > 0 ? row.IP : row.NoteKey);
            _ws.Say($"{row.Shown} copiato.");
        }
    }

    /// <summary>"Apri" nella scheda: un menu con le porte che l'host ha aperte.</summary>
    private void OnOpenMenu(Control anchor)
    {
        if (_shown is not { } row)
        {
            return;
        }

        ContextMenu menu = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, PlacementTarget = anchor };

        foreach (HostDoor door in Doors(row))
        {
            menu.Items.Add(MenuEntry(door.Label, DoorIcon(door), "Blue", true, () => Open(door)));
        }

        menu.Open(anchor);
    }

    /// <summary>"Altro" nella scheda: copiare l'indirizzo, la mappa, mettere da parte.</summary>
    private void OnMoreMenu(Control anchor)
    {
        if (_shown is not { } row)
        {
            return;
        }

        ContextMenu menu = new() { Placement = PlacementMode.BottomEdgeAlignedRight, PlacementTarget = anchor };
        menu.Items.Add(MenuEntry("Copia indirizzo", "copy", null, true, () => CopyAddress(row)));

        if (_ws.MapHostOf(row) is { } onMap)
        {
            menu.Items.Add(MenuEntry("Mostra sulla mappa", "map", "Teal", true, () =>
            {
                MapWanted?.Invoke(onMap);
                return Task.CompletedTask;
            }));
        }

        menu.Items.Add(GroupsMenu(row));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry($"Metti da parte {row.Shown}", "hide", "Red", true, () =>
        {
            _ws.Hide(Target(row));
            return Task.CompletedTask;
        }));
        menu.Open(anchor);
    }

    /// <summary>"Esporta" nella barra: gli host mostrati nella nota (come tabella) o in un file.</summary>
    private void OnExportMenu()
    {
        ContextMenu menu = new() { Placement = PlacementMode.BottomEdgeAlignedRight, PlacementTarget = _export };
        menu.Items.Add(MenuEntry("Tabella nella nota", "table", "Accent", !_ws.IsLocked, () =>
        {
            OnToNote();
            return Task.CompletedTask;
        }));
        menu.Items.Add(MenuEntry("File CSV…", "export", null, true, OnExport));
        menu.Open(_export);
    }

    private static Button CardButton(string icon, string? text, string tip, bool chevron = false)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icon(icon, 12) } };

        if (text is not null)
        {
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        }

        if (chevron)
        {
            content.Children.Add(Icon("down", 8));
        }

        Button button = new()
        {
            Theme = Themes.Of(text is null ? "Tool" : "Soft"),
            Height = 28,
            MinWidth = 28,
            Padding = new Thickness(text is null ? 0 : 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = content
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Dictionary<string, object?> Reminder(HostRow row)
    {
        Dictionary<string, object?> seed = new()
        {
            ["kind"] = "event",
            ["link"] = new Dictionary<string, string> { ["kind"] = "host", ["host"] = row.Shown, ["name"] = row.Label == row.Shown ? string.Empty : row.Label }
        };

        if (DuckNote.Notes.Calendar.Agenda.Day(row.TlsExpiry) is { } expires && expires >= DateOnly.FromDateTime(DateTime.Now))
        {
            seed["kind"] = "deadline";
            seed["title"] = $"Rinnovare il certificato di {row.Shown}";
            DateOnly before = expires.AddDays(-7);
            seed["date"] = DuckNote.Notes.Calendar.Agenda.Iso(before >= DateOnly.FromDateTime(DateTime.Now) ? before : expires);
        }

        return seed;
    }

    // ------------------------------------------------------------------ la scheda dell'host

    /// <summary>
    /// Accanto alla tabella se resta posto per lei (almeno 560 punti), altrimenti sopra, a destra,
    /// come la scheda di un apparato sulla mappa.
    /// </summary>
    private void Dock(double width)
    {
        bool docked = width - 250 - 14 - 14 - 340 >= 560;
        if (docked == _docked)
        {
            return;
        }

        _docked = docked;
        _pageGrid.ColumnDefinitions[3].Width = new GridLength(docked ? 14 : 0);
        _pageGrid.ColumnDefinitions[4].Width = new GridLength(docked ? 340 : 0);
        Grid.SetColumn(_hostCard, docked ? 4 : 2);
        _hostCard.Width = docked ? double.NaN : 340;
        _hostCard.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        _hostCard.Margin = docked ? default : new Thickness(0, 8, 8, 44);
        _hostCard.ZIndex = docked ? 0 : 5;
        _hostCard.BoxShadow = docked ? default : BoxShadows.Parse("0 10 30 0 #33000000");
        _hostCard.IsVisible = docked || _shown is not null;
    }

    private void CloseCard()
    {
        _table.SelectedItem = null;
        Show(null);
    }

    // ------------------------------------------------------------------ gruppi

    /// <summary>Il secondo comando analizza il gruppo scelto (o la nota); le schede dei gruppi si ridisegnano.</summary>
    private void ShowGroup()
    {
        ScanGroup? group = _ws.Groups.Find(_group);

        if (group is null && _group is not (AllGroups or NoGroup or ScanGroups.Note))
        {
            _group = AllGroups;
        }

        _noteIcon.Kind = group is null ? "check" : "group";
        _noteText.Text = group is null ? "Controlla la nota" : $"Controlla «{Short(group.Name, 22)}»";
        ToolTip.SetTip(_note, group is null
            ? "Solo gli indirizzi scritti nelle pagine della nota"
            : group.Targets.Count == 0 ? "Il gruppo e' vuoto: aggiungi reti o indirizzi con Modifica" : $"Analizza {Short(string.Join(", ", group.Targets), 120)}");
    }

    /// <summary>
    /// Crea (con <paramref name="group"/> null) o modifica un gruppo; il nuovo diventa quello
    /// scelto. <paramref name="start"/>: le righe di un gruppo nuovo (un host della tabella);
    /// senza, l'intervallo scritto in alto.
    /// </summary>
    private async Task EditGroup(ScanGroup? group, string? start = null)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        List<ScanGroup> all = [.. _ws.Groups.All];
        string suggested = group is null ? start ?? _range.Text?.Trim() ?? string.Empty : string.Empty;
        GroupChoice? choice = await GroupDialog.Ask(owner, group, all, suggested);

        if (choice is null)
        {
            return;
        }

        if (choice.Removed)
        {
            all.RemoveAll(other => other.Id == choice.Group.Id);
            _group = AllGroups;
            _ws.SaveGroups(all);
            _ws.Say($"Gruppo «{choice.Group.Name}» eliminato: gli host trovati restano in tabella.");
            return;
        }

        int at = all.FindIndex(other => other.Id == choice.Group.Id);

        if (at >= 0)
        {
            all[at] = choice.Group;
        }
        else
        {
            all.Add(choice.Group);
        }

        _group = choice.Group.Id;
        _ws.SaveGroups(all);
        _ws.Say(at >= 0 ? $"Gruppo «{choice.Group.Name}» aggiornato." : $"Gruppo «{choice.Group.Name}» creato: Analizza lo controlla tutto.");
    }

    private static string Short(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";

    private List<IReadOnlyList<string>> ShownRows() =>
    [
        .. _table.ItemsSource is null ? [] : SortedRows().Select(row => (IReadOnlyList<string>)
        [
            row.IP, row.Status, row.Hostname, row.NetBiosName, row.MdnsName, row.Mac, row.Vendor, row.RoundTrip,
            row.Ttl, row.Os, row.DeviceType, row.OpenPorts, row.Services, row.HttpTitle, row.LastSeen
        ])
    ];

    /// <summary>Le righe nell'ordine in cui la tabella le mostra (anche ordinate per colonna).</summary>
    private IEnumerable<HostRow> SortedRows() =>
        _table.CollectionView is { } view ? view.OfType<HostRow>() : _visible;

    private async Task OnExport()
    {
        List<IReadOnlyList<string>> rows = ShownRows();

        if (rows.Count == 0)
        {
            _ws.Say("Niente da esportare: la tabella e' vuota.");
            return;
        }

        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta gli host",
            SuggestedFileName = $"ducknote-host-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV (separato da punto e virgola)") { Patterns = ["*.csv"], AppleUniformTypeIdentifiers = ["public.comma-separated-values-text"] }]
        });

        if (file is null)
        {
            return;
        }

        try
        {
            byte[] bytes = CsvTable.Write(CsvHeader, rows);
            await using (Stream stream = await file.OpenWriteAsync())
            {
                await stream.WriteAsync(bytes);
            }

            Notices.Current?.Success("Esportazione fatta", $"{rows.Count} host in {file.Name}.");
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            Notices.Current?.Error("Esportazione non riuscita", problem.Message);
        }
    }

    private void OnToNote()
    {
        List<IReadOnlyList<string>> rows =
        [
            .. SortedRows().Select(row => (IReadOnlyList<string>)[row.IP.Length > 0 ? row.IP : row.NoteKey, row.Name, row.Status, row.Os, row.OpenPorts])
        ];

        if (rows.Count == 0)
        {
            _ws.Say("Niente da portare nella nota: la tabella e' vuota.");
            return;
        }

        _ws.SendTable(["Indirizzo", "Nome", "Stato", "Sistema", "Porte aperte"], rows);
    }

    // ------------------------------------------------------------------ la tabella

    private static DataGrid HostTable()
    {
        DataGrid grid = new()
        {
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 34,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12
        };
        grid.Classes.Add("hosts");

        DataGridTemplateColumn address = new()
        {
            Header = "Indirizzo",
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star),
            MinWidth = 150,
            SortMemberPath = nameof(HostRow.SortKey),
            CellTemplate = new FuncDataTemplate<HostRow>((_, _) =>
            {
                // davanti all'indirizzo: verde online, arancio da verificare, rosso offline
                Ellipse dot = new() { Width = 9, Height = 9, Margin = new Thickness(12, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.Bind(Shape.FillProperty, new Binding(nameof(HostRow.Tone)) { Converter = TokenBrush.Instance });
                dot.Bind(ToolTip.TipProperty, new Binding(nameof(HostRow.ToneText)));

                TextBlock text = new() { FontFamily = Mono, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                text.Bind(TextBlock.TextProperty, new Binding(nameof(HostRow.Address)));

                Glyph note = new() { Kind = "note", Width = 11, Height = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                note.Bind(Glyph.ForegroundProperty, note.GetResourceObservable("Accent"));
                note.Bind(IsVisibleProperty, new Binding(nameof(HostRow.FromNote)));
                ToolTip.SetTip(note, "Scritto nella nota");

                return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { dot, text, note } };
            }, supportsRecycling: true)
        };
        grid.Columns.Add(address);
        grid.Columns.Add(TextColumn("Nome", nameof(HostRow.Label), 1.4, 80));
        grid.Columns.Add(TextColumn("Risposta", nameof(HostRow.RoundTripShort), 0.7, 88, nameof(HostRow.AverageMs)));
        grid.Columns.Add(TextColumn("Sistema", nameof(HostRow.Os), 1.3, 70));
        grid.Columns.Add(TextColumn("Porte", nameof(HostRow.OpenPorts), 1.2, 70, nameof(HostRow.PortCount)));
        return grid;
    }

    private static DataGridTextColumn TextColumn(string header, string path, double stars, double min, string? sortBy = null) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(stars, DataGridLengthUnitType.Star),
        MinWidth = min,
        SortMemberPath = sortBy ?? path,
        FontSize = 12
    };

    // ------------------------------------------------------------------ pezzi

    private static FontFamily Mono =>
        Application.Current?.TryFindResource("Mono", out object? family) == true && family is FontFamily mono ? mono : FontFamily.Default;

    private static TextBlock Caption(string text)
    {
        TextBlock block = new() { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        block.Classes.Add("caption");
        return block;
    }

    private static TextBlock Trimmed(TextBlock block, Thickness margin)
    {
        block.Margin = margin;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }

    private static Glyph Icon(string kind, double size) => new() { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    private static Button Labelled(string theme, string icon, string text, string tip) =>
        Labelled(theme, Icon(icon, 11), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, tip);

    private static Button Labelled(string theme, Glyph icon, TextBlock text, string tip)
    {
        Button button = new()
        {
            Theme = Themes.Of(theme),
            Height = 34,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { icon, text } }
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button ToolButton(string icon, string tip)
    {
        Button button = new() { Theme = Themes.Of("Tool"), Width = 34, Height = 34, Content = Icon(icon, 15) };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Rectangle Separator()
    {
        Rectangle line = new() { Width = 1, Height = 20, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Bind(Shape.FillProperty, line.GetResourceObservable("Separator"));
        return line;
    }

    private static TextBlock Counted(string label, Run count)
    {
        TextBlock text = new();
        text.Inlines!.Add(new Run(label));
        text.Inlines.Add(count);
        return text;
    }

    /// <summary>
    /// Un pulsante dei filtri di stato. Il filtro scelto lo tiene <see cref="_state"/>: quando un
    /// pulsante si accende, quello di prima e' ancora acceso (Avalonia spegne gli altri del gruppo
    /// dopo aver avvisato), e chiedere ai pulsanti quale e' acceso dava il filtro vecchio.
    /// </summary>
    private RadioButton StateSegment(HostState state, object content, bool chosen = false)
    {
        RadioButton button = new() { Theme = Themes.Of("Segment"), GroupName = "state", Content = content, IsChecked = chosen };

        button.IsCheckedChanged += (_, _) =>
        {
            if (button.IsChecked == true && _state != state)
            {
                _state = state;
                ShowSession();
            }
        };

        return button;
    }

    private static Border Well(params RadioButton[] segments) => Segments.Well(default, segments);

    private static MenuItem MenuEntry(string header, string icon, string? tone, bool enabled, Func<Task> run)
    {
        MenuItem item = new() { Header = header, IsEnabled = enabled, Icon = MenuIcon(icon, tone) };
        item.Click += async (_, _) => await run();
        return item;
    }

    private static Glyph MenuIcon(string icon, string? tone)
    {
        Glyph glyph = new() { Kind = icon, Width = 13, Height = 13 };

        if (tone is not null)
        {
            glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(tone));
        }

        return glyph;
    }

    private static void Shake(Control control)
    {
        TranslateTransform shift = control.RenderTransform as TranslateTransform ?? new TranslateTransform();
        control.RenderTransform = shift;
        Motion.Tween((control, "shake"), 0, 1, 420, t => shift.X = Math.Sin(t * Math.PI * 6) * 7 * (1 - t), () => shift.X = 0);
    }

    private static T Cell<T>(T control, int column, Thickness? margin = null) where T : Control
    {
        Grid.SetColumn(control, column);

        if (margin is { } space)
        {
            control.Margin = space;
        }

        return control;
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T Span<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, 3);
        return control;
    }
}
