using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DuckNote.App.Scanning;
using DuckNote.App.Storage;
using DuckNote.App.Theme;
using DuckNote.App.Views;
using DuckNote.Scan;

namespace DuckNote.App;

public partial class MainWindow : Window
{
    private const string DarkGlyph =
        "M6,10 A4,4 0 1 1 14,10 A4,4 0 1 1 6,10 Z M10,0.8 V3 M10,17 V19.2 M0.8,10 H3 M17,10 H19.2 " +
        "M3.5,3.5 L5,5 M15,15 L16.5,16.5 M16.5,3.5 L15,5 M5,15 L3.5,16.5";

    private const string LightGlyph = "M 10,2 A 8,8 0 1 0 18,10 A 6,6 0 1 1 10,2 Z";

    private readonly ScanSession _session = new();
    private readonly AppSettings _settings;
    private readonly bool _encryptAfterLoad;
    private string _theme;

    public MainWindow(AppSettings settings, string theme, Vault.VaultSession vault, bool encryptAfterLoad)
    {
        _settings = settings;
        _theme = theme;
        _vault = vault;
        _encryptAfterLoad = encryptAfterLoad;

        InitializeComponent();

        NetGrid.ItemsSource = _session.Rows;
        _session.PropertyChanged += OnSessionChanged;
        _session.Rows.CollectionChanged += (_, _) => RefreshCounts();

        Wire();
        Loaded += OnLoaded;
    }

    private void Wire()
    {
        BtnMin.Click += OnMinimise;
        BtnZoom.Click += OnZoom;
        BtnClose.Click += OnClose;
        BtnTheme.Click += OnToggleTheme;

        TabNote.Checked += OnTabNote;
        TabNet.Checked += OnTabNet;

        BtnScan.Click += OnScanNote;
        BtnScanNote.Click += OnScanNote;
        BtnScanRange.Click += OnScanRange;
        BtnStop.Click += OnStop;
        RangeBox.KeyDown += OnRangeKeyDown;

        BindHint(RangeBox, RangeHint);
        BindHint(SideSearch, SideSearchHint);
        BindHint(GridFilter, GridFilterHint);
        BindHint(FindBox, FindHint);
        BindHint(ReplBox, ReplHint);

        WireDetail();
    }

    private DetailDock? _detail;

    private void WireDetail()
    {
        _detail = new DetailDock(this, Resources) { Width = _settings.InspectorWidth };

        NetGrid.SelectionChanged += (_, _) =>
        {
            if (NetGrid.SelectedItem is not Models.ScanRow row)
            {
                return;
            }

            Reveal(row);
        };

        SideList.SelectionChanged += (_, _) =>
        {
            if (_syncingSelection || SideList.SelectedItem is not Models.SideItem { IP.Length: > 0 } item)
            {
                return;
            }

            Models.ScanRow? row = _session.Rows.FirstOrDefault(candidate => candidate.IP == item.IP);
            if (row is null)
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                NetGrid.SelectedItem = row;
                NetGrid.ScrollIntoView(row);
            }
            finally
            {
                _syncingSelection = false;
            }

            Reveal(row);
        };

        NetGrid.PreviewMouseLeftButtonDown += (_, e) => ToggleIfSame(RowUnder(e.OriginalSource as DependencyObject));
        SideList.PreviewMouseLeftButtonDown += (_, e) => ToggleIfSame(ItemUnder(e.OriginalSource as DependencyObject));

        BtnInspect.Click += (_, _) =>
        {
            _detail.Toggle();
            UpdateInspectGlyph();
        };

        LocationChanged += (_, _) => RequestSettle();
        SizeChanged += (_, _) => RequestSettle();
        StateChanged += (_, _) => _detail.Follow();
    }

    private bool _syncingSelection;

    private static string? RowUnder(DependencyObject? source) =>
        (Ancestor<DataGridRow>(source))?.Item is Models.ScanRow row ? row.IP : null;

    private static string? ItemUnder(DependencyObject? source) =>
        (Ancestor<ListBoxItem>(source))?.DataContext is Models.SideItem { IP.Length: > 0 } item ? item.IP : null;

    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as T;
    }

    private void ToggleIfSame(string? host)
    {
        if (host is null || _detail is null || NetGrid.SelectedItem is not Models.ScanRow shown
            || !string.Equals(shown.IP, host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_detail.IsOpen)
        {
            _detail.Close();
            UpdateInspectGlyph();
            return;
        }

        Reveal(shown);
    }

    private void Reveal(Models.ScanRow row)
    {
        if (_detail is null)
        {
            return;
        }

        _detail.Show(row);
        if (!_detail.IsOpen)
        {
            _detail.Open();
        }

        UpdateInspectGlyph();
    }

    private void UpdateInspectGlyph()
    {
        if (_detail is null)
        {
            return;
        }

        if (_detail.IsOpen)
        {
            GlyphInspect.SetResourceReference(Shape.StrokeProperty, "Accent");
            GlyphInspect.StrokeThickness = 1.8;
            return;
        }

        GlyphInspect.SetResourceReference(Shape.StrokeProperty, "LabelSecondary");
        GlyphInspect.StrokeThickness = 1.4;
    }

    private static void BindHint(TextBox field, TextBlock hint)
    {
        void Sync() =>
            hint.Visibility = field.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

        field.TextChanged += (_, _) => Sync();
        Sync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _sidebarWidth = _settings.SidebarWidth;
        RangeBox.Text = _settings.LastRange;

        ApplyTheme(_theme);
        TabNote.IsChecked = true;
        ShowView(network: false, immediate: true);

        SetUpChrome();
        SetUpEditor();

        if (_encryptAfterLoad && _vault.CommitPending(Editor.Document is null
                ? []
                : NoteXaml()))
        {
            _noteDirty = false;
            StatusText.Text = "Cifratura attiva: la nota vive nel contenitore, i file in chiaro sono stati rimossi.";
        }

        SetUpSidebar();
        SyncNoteHosts();
        SetUpVault();
        StartMonitor();
        RefreshCounts();

        _settings.Save();
    }

    private void ApplyTheme(string theme)
    {
        _theme = theme;
        ThemeTokens.Apply(Resources, theme);
        _editorBrushes.Sync(theme);

        try
        {
            ThemeGlyph.Data = Geometry.Parse(theme == "dark" ? DarkGlyph : LightGlyph);
        }
        catch (FormatException)
        {
            return;
        }

        SolidColorBrush ink = ThemeTokens.Brush(ThemeTokens.Colour(theme, "LabelSecondary"));
        if (theme == "dark")
        {
            ThemeGlyph.Fill = null;
            ThemeGlyph.Stroke = ink;
            ThemeGlyph.StrokeThickness = 1.3;
        }
        else
        {
            ThemeGlyph.Stroke = null;
            ThemeGlyph.Fill = ink;
        }
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        _settings.FollowSystemTheme = false;
        _settings.Theme = _theme == "dark" ? "light" : "dark";

        ApplyTheme(_settings.Theme);
        UpdateMonitorChip();
        RefreshSideList();
        _settings.Save();
    }

    private void ShowView(bool network, bool immediate = false)
    {
        Motion.MovePill(SegPillT, SegPillS, network ? 92 : 0, immediate);

        if (network)
        {
            ViewNotes.Visibility = Visibility.Collapsed;
            ViewNet.Visibility = Visibility.Visible;
            Motion.Enter(ViewNet, ViewNetT, from: 18);
            return;
        }

        ViewNet.Visibility = Visibility.Collapsed;
        ViewNotes.Visibility = Visibility.Visible;
        Motion.Enter(ViewNotes, ViewNotesT, from: -18);
    }

    private void OnTabNote(object sender, RoutedEventArgs e) => ShowView(network: false);

    private void OnTabNet(object sender, RoutedEventArgs e) => ShowView(network: true);

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        StatusText.Text = _session.Message;
        BtnScanRange.IsEnabled = _session.IsIdle;
        BtnScan.IsEnabled = _session.IsIdle;
        BtnStop.IsEnabled = _session.IsRunning;
        RangeBox.IsEnabled = _session.IsIdle;
        ScanProgress.Value = _session.Progress;
        ScanProgress.Visibility = _session.IsRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncNoteHosts() => _session.EnsurePending(NoteHosts());

    private void RefreshCounts()
    {
        StatHosts.Text = _session.Rows.Count.ToString();
        StatUp.Text = _session.UpCount.ToString();
        StatDown.Text = _session.DownCount.ToString();
        CountText.Text = _session.Rows.Count == 0
            ? string.Empty
            : $"{_session.Rows.Count} host";

        foreach (Models.ScanRow row in _session.Rows)
        {
            if (row.StatusRank >= Models.ScanRow.NeverChecked)
            {
                continue;
            }

            _hostStates[row.IP] = row.StatusRank <= 1;
            if (row.NoteKey.Length > 0)
            {
                _hostStates[row.NoteKey] = row.StatusRank <= 1;
            }
        }

        RecolourHosts();
        RefreshSideList();
    }

    private async void OnScanRange(object sender, RoutedEventArgs e) => await ScanAsync();

    private async void OnRangeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _session.IsIdle)
        {
            await ScanAsync();
        }
    }

    private async void OnScanNote(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> hosts = NoteHosts();
        if (hosts.Count == 0)
        {
            StatusText.Text = "Nella nota non ci sono indirizzi da analizzare.";
            return;
        }

        await ScanAsync(string.Join(',', hosts));
    }

    private async Task ScanAsync(string? targets = null)
    {
        TabNet.IsChecked = true;

        if (targets is null)
        {
            _settings.LastRange = RangeBox.Text;
            _settings.Save();
        }

        await _session.RunAsync(targets ?? RangeBox.Text, _settings.ToScanOptions());

        SyncNoteHosts();
        RefreshCounts();
    }

    private void OnStop(object sender, RoutedEventArgs e) => _session.Stop();

    private void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnZoom(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private bool _leaving;

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_leaving)
        {
            return;
        }
        _leaving = true;

        _detail?.Dispose();

        ScaleTransform shrink = Motion.Scalable(RootBorder);
        IEasingFunction ease = Motion.Ease(mode: EasingMode.EaseIn);

        Motion.Grow(shrink, 0.97, 150);

        DoubleAnimation fade = Motion.Slide(1, 0, 150, ease);
        fade.Completed += (_, _) => Close();
        RootBorder.BeginAnimation(OpacityProperty, fade);
    }

    internal bool RescueNote()
    {
        try
        {
            SaveNote();
            _vault.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _monitorTimer?.Stop();
        _idleTimer?.Stop();
        _session.Stop();
        SaveNote();
        _vault.Dispose();

        _settings.SidebarMode = _outlineMode ? "outline" : "host";
        if (ColSidebar.ActualWidth > 1)
        {
            _settings.SidebarWidth = (int)ColSidebar.ActualWidth;
        }

        if (_detail is { Width: > 1 })
        {
            _settings.InspectorWidth = (int)_detail.Width;
        }
        _detail?.Dispose();

        _settings.Save();

        base.OnClosing(e);
    }
}
