using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DuckNote.App.Dialogs;
using DuckNote.App.Pages;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using Shape = System.Windows.Shapes.Shape;

namespace DuckNote.App;

/// <summary>
/// Shell Duck. Le pagine si registrano in <see cref="RegisterPages"/>: chiave, titolo, glifo
/// Segoe Fluent, token del colore della tile, fabbrica e pannello della sidebar (o nessuno). Le pagine vengono
/// create una volta sola, preparate a priorita' bassa dopo l'avvio (Prewarm) e riusate.
/// </summary>
public partial class MainWindow : Window
{
    private const string AppTitle = "DuckNote";
    private const string Moon = "M 10,2 A 8,8 0 1 0 18,10 A 6,6 0 1 1 10,2 Z";
    private const string Sun = "M6,10 A4,4 0 1 1 14,10 A4,4 0 1 1 6,10 Z M10,0.8 V3 M10,17 V19.2 M0.8,10 H3 M17,10 H19.2 " +
                               "M3.5,3.5 L5,5 M15,15 L16.5,16.5 M16.5,3.5 L15,5 M5,15 L3.5,16.5";

    private sealed record PageInfo(string Key, string Title, Func<FrameworkElement> Factory, Button? Button);

    private readonly List<PageInfo> _pages = [];
    private readonly Dictionary<string, FrameworkElement> _cache = [];
    private readonly Dictionary<string, Dictionary<string, (Border Box, TextBlock Text)>> _badges = [];
    private readonly DuckFlock _flock;
    private string? _current;
    private string? _previous;

    /// <summary>Clic sul riquadro account (login/logout a cura dell'app).</summary>
    public event Action? AccountClicked;

    /// <summary>Chiusura: restituire false per annullarla (es. lavori in corso).</summary>
    public Func<bool>? CanClose { get; set; }

    private readonly Workspace _ws;
    private readonly DispatcherTimer _duckRest = new() { Interval = TimeSpan.FromMilliseconds(900) };

    public MainWindow(Workspace ws)
    {
        _ws = ws;
        InitializeComponent();
        Toast.Attach(ToastHost);
        _flock = new DuckFlock(DuckLayer);
        ThemeManager.Changed += OnThemeChanged;
        OnThemeChanged(ThemeManager.Effective);

        RegisterPages();
        WireWorkspace();

        ThemeManager.TranslucentChanged += _ => ApplyGlass();
        SourceInitialized += (_, _) => ApplyGlass();
        Loaded += (_, _) =>
        {
            RebuildDucks();
            Start();
            Prewarm();
        };
    }

    /// <summary>
    /// Unico punto da modificare per aggiungere pagine. Rete sta in cima alla sidebar; la nota
    /// si apre dai fogli al centro, le Impostazioni dall'ingranaggio in alto a destra.
    /// </summary>
    private void RegisterPages()
    {
        AddPage("network", "Rete", "\uE774", "Blue", () => new NetworkPage(_ws), NavTop);
        AddPage("notes", "Note", "\uE70F", "Orange", () => new NotesPage(_ws), null);
        AddPage("settings", "Impostazioni", "\uE713", "Gray", () => new SettingsPage(_ws), null);
    }

    // ---------------------------------------------------------------- registrazione
    /// <param name="tileToken">Token di colore della tile (Orange, Blue, Purple, Teal, Green, Red, Pink, Gray...).</param>
    /// <param name="host">Dove mettere la voce; null = pagina senza voce nella sidebar.</param>
    /// <param name="iconOnly">Solo la tessera, col titolo nel tooltip (niente testo, niente contatore).</param>
    public void AddPage(string key, string title, string glyph, string tileToken, Func<FrameworkElement> factory, Panel? host, bool iconOnly = false)
    {
        if (host is null)
        {
            _pages.Add(new PageInfo(key, title, factory, null));
            return;
        }

        var tile = new Border { Style = (Style)FindResource("IconTile") };
        tile.SetResourceReference(Border.BackgroundProperty, tileToken);
        tile.Child = new TextBlock { Text = glyph, Style = (Style)FindResource("TileGlyph") };
        if (iconOnly)
        {
            tile.Margin = new Thickness(0);
            tile.Width = tile.Height = 28;
            ((TextBlock)tile.Child).FontSize = 14;
            var square = new Button
            {
                Style = (Style)FindResource("IconNavItem"), Content = tile, ToolTip = title,
            };
            square.Click += (_, _) => Navigate(key);
            _pages.Add(new PageInfo(key, title, factory, square));
            host.Children.Add(square);
            return;
        }

        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };

        // contatori a destra della voce, uno per tono (Red, Green...), nascosti a zero
        var counters = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var tones = new Dictionary<string, (Border Box, TextBlock Text)>();
        foreach (var tone in new[] { "Red", "Green", "Blue", "Orange" })
        {
            var badgeText = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, tone);
            var badge = new Border { Style = (Style)FindResource("Badge"), Visibility = Visibility.Collapsed, Child = badgeText, Margin = new Thickness(4, 0, 0, 0) };
            badge.SetResourceReference(Border.BackgroundProperty, tone + "Soft");
            tones[tone] = (badge, badgeText);
            counters.Children.Add(badge);
        }
        _badges[key] = tones;

        var content = new Grid();
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { tile, label } });
        content.Children.Add(counters);

        var b = new Button { Style = (Style)FindResource("NavItem"), Content = content };
        b.Click += (_, _) => Navigate(key);
        _pages.Add(new PageInfo(key, title, factory, b));
        host.Children.Add(b);
    }

    /// <summary>Un contatore sulla voce (0 = nascosto). tone: Red | Green | Blue | Orange; i toni convivono.</summary>
    public void SetBadge(string key, int count, string tone = "Blue", string? tooltip = null)
    {
        if (!_badges.TryGetValue(key, out var tones) || !tones.TryGetValue(tone, out var bd)) return;
        bd.Box.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bd.Text.Text = count > 999 ? "999+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        bd.Box.ToolTip = tooltip;
    }

    // ---------------------------------------------------------------- navigazione
    /// <summary>La pagina per quella chiave, creata alla prima richiesta e poi riusata.</summary>
    private FrameworkElement Open(string key)
    {
        if (!_cache.TryGetValue(key, out var page))
        {
            _cache[key] = page = _pages.First(p => p.Key == key).Factory();
        }

        return page;
    }

    public void Navigate(string key)
    {
        if (_pages.FirstOrDefault(p => p.Key == key) is not { } info) return;
        if (_current is not null && _current != key && _cache.GetValueOrDefault(_current) is NotesPage leaving) leaving.OnHidden();

        _current = key;
        foreach (var p in _pages)
        {
            if (p.Button is not null) p.Button.Tag = p.Key == key ? "Active" : null;
        }

        var page = Open(key);
        Title = $"{AppTitle} - {info.Title}";
        PageHost.Content = page;
        var order = _pages.Select(p => p.Key).ToList();
        // direzionale: verso il basso della sidebar entra da destra, verso l'alto da sinistra
        Fx.PageIn(page, _previous is null || order.IndexOf(key) >= order.IndexOf(_previous) ? 1 : -1);
        _previous = key;
        if (page is IDuckPage p2) p2.OnShown();
    }

    /// <summary>
    /// Costruisce e misura fuori schermo le pagine non ancora aperte, una per volta, a priorita'
    /// ApplicationIdle: la prima apertura non paga la creazione del XAML durante l'animazione.
    /// </summary>
    private void Prewarm()
    {
        var pending = _pages.Select(p => p.Key).Where(k => !_cache.ContainsKey(k)).ToList();
        void Next()
        {
            if (pending.Count == 0) return;
            var key = pending[0];
            pending.RemoveAt(0);
            if (!_cache.ContainsKey(key))
            {
                try
                {
                    var page = Open(key);
                    page.Measure(new Size(PageHost.ActualWidth, PageHost.ActualHeight));
                    page.Arrange(new Rect(0, 0, PageHost.ActualWidth, PageHost.ActualHeight));
                }
                catch (Exception ex)
                {
                    // mai bloccare l'avvio: la pagina verra' ricostruita al primo clic
                    _cache.Remove(key);
                    System.Diagnostics.Debug.WriteLine($"Prewarm '{key}': {ex}");
                }
            }
            Dispatcher.BeginInvoke(Next, DispatcherPriority.ApplicationIdle);
        }
        Dispatcher.BeginInvoke(Next, DispatcherPriority.ApplicationIdle);
    }

    // ---------------------------------------------------------------- stato, account
    /// <summary>Riga di stato in basso (ultimo evento) e testo piccolo in alto a destra (versione...).</summary>
    public void SetStatus(string text, string? right = null)
    {
        StatusText.Text = text;
        if (right is not null) StatusRight.Text = right;
    }

    /// <summary>Scheda in fondo alla sidebar. progress: 0..1 oppure null per nasconderla. dotToken: Green | Blue | Orange | Red.</summary>
    public void SetStatusCard(string title, string text, double? progress, string dotToken = "Green", string right = "", bool pulse = false)
    {
        StatusCard.Visibility = Visibility.Visible;
        CardTitle.Text = title;
        CardText.Text = text;
        CardRight.Text = right;
        CardProgress.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;
        CardProgress.Value = progress ?? 0;
        CardDot.SetResourceReference(Shape.FillProperty, dotToken);
        Fx.Pulse(CardDot, pulse);
    }

    private void OnAccount(object sender, RoutedEventArgs e) => AccountClicked?.Invoke();

    public bool DucksAnimate { get => _flock.Animate; set => _flock.Animate = value; }

    // ---------------------------------------------------------------- tema
    private void OnTheme(object sender, RoutedEventArgs e) => ThemeManager.Toggle();
    private void OnSettings(object sender, RoutedEventArgs e) => Navigate("settings");
    private void OnThemeSystem(object sender, MouseButtonEventArgs e) { ThemeManager.Set("system"); e.Handled = true; }

    private void OnThemeChanged(string theme)
    {
        if (theme == "dark")
        {
            ThemeGlyph.Data = Geometry.Parse(Sun);
            ThemeGlyph.Fill = null;
            ThemeGlyph.SetResourceReference(Shape.StrokeProperty, "LabelSecondary");
            ThemeGlyph.StrokeThickness = 1.3;
        }
        else
        {
            ThemeGlyph.Data = Geometry.Parse(Moon);
            ThemeGlyph.Stroke = null;
            ThemeGlyph.SetResourceReference(Shape.FillProperty, "LabelSecondary");
        }
    }

    // ---------------------------------------------------------------- finestra
    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (CanClose?.Invoke() == false) return;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _flock.Animate = false;
        _ws.Shutdown();
        base.OnClosing(e);
    }

    private void OnMin(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMax(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Vetro: sfocatura di Windows dietro la finestra + angoli compatibili con quelli di DWM.</summary>
    private void ApplyGlass()
    {
        var on = ThemeManager.Translucent && Backdrop.Apply(this, true);
        if (!ThemeManager.Translucent) Backdrop.Apply(this, false);
        _glass = on;
        OnStateChanged(EventArgs.Empty);
    }

    private bool _glass;

    /// <summary>Raggio degli angoli: con il vetro segue quelli di Windows (8 su Win11, dritti su Win10).</summary>
    private CornerRadius Corners => WindowState == WindowState.Maximized ? new CornerRadius(0)
        : _glass ? new CornerRadius(Backdrop.IsWindows11 ? 8 : 0) : new CornerRadius(12);

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // massimizzata senza bordo: Windows sborda di ~6 px, angoli dritti
        RootBorder.CornerRadius = Corners;
        Lock.Corners = new CornerRadius(Math.Max(0, Corners.TopLeft - 1));
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(6) : new Thickness(0);
    }
}
