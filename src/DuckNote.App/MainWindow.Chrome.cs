using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DuckNote.App.Theme;

namespace DuckNote.App;

public partial class MainWindow
{
    private const double SidebarMinimum = 170;

    private double _sidebarWidth = 232;
    private bool _sidebarAnimating;
    private System.Windows.Threading.DispatcherTimer? _monitorTimer;

    private Ducks.DuckPond? _pond;
    private System.Windows.Threading.DispatcherTimer? _duckRest;

    private void RebuildDucks()
    {
        _pond?.Build(_settings.DuckBackground, _settings.DuckCount, _settings.DuckOpacity, _theme);
        _pond?.Start();
    }

    private void RestDucks()
    {
        _pond?.Stop();
        _duckRest?.Stop();
        _duckRest?.Start();
    }

    private void SetUpLogo()
    {
        if (Assets.DuckImage.Bitmap is { } duck)
        {
            LogoImage.Source = duck;
            LogoImage.Visibility = Visibility.Visible;
            LogoVector.Visibility = Visibility.Collapsed;
            return;
        }

        LogoImage.Visibility = Visibility.Collapsed;
        LogoVector.Visibility = Visibility.Visible;
    }

    private void ToggleSidebar()
    {
        bool open = SidebarPanel.Visibility == Visibility.Visible && ColSidebar.ActualWidth > 1;

        if (open)
        {
            _sidebarWidth = ColSidebar.ActualWidth;
            SideInner.Width = _sidebarWidth;
            AnimateSidebar(0);
            return;
        }

        SidebarPanel.Visibility = Visibility.Visible;
        SideInner.Width = _sidebarWidth;
        AnimateSidebar(_sidebarWidth);
    }

    private void AnimateSidebar(double to, int milliseconds = 190)
    {
        ColSidebar.MinWidth = 0;
        _sidebarAnimating = true;

        GridLengthAnimation animation = new()
        {
            From = ColSidebar.ActualWidth,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = Motion.Ease()
        };

        animation.Completed += (_, _) =>
        {
            ColSidebar.BeginAnimation(ColumnDefinition.WidthProperty, null);
            ColSidebar.Width = new GridLength(to);

            if (to <= 0)
            {
                SidebarPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ColSidebar.MinWidth = SidebarMinimum;
            }

            _sidebarAnimating = false;
        };

        ColSidebar.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private System.Windows.Threading.DispatcherTimer? _settle;
    private bool _ducksNeedRebuild;

    private void RequestSettle()
    {
        _pond?.Stop();

        _settle ??= Rest(90, () =>
        {
            if (_ducksNeedRebuild)
            {
                _ducksNeedRebuild = false;
                RebuildDucks();
            }
            else
            {
                _pond?.Start();
            }

            _detail?.Follow();
        });

        _settle.Stop();
        _settle.Start();
    }

    private static System.Windows.Threading.DispatcherTimer Rest(int milliseconds, Action then)
    {
        System.Windows.Threading.DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };

        timer.Tick += (sender, _) =>
        {
            ((System.Windows.Threading.DispatcherTimer)sender!).Stop();
            then();
        };

        return timer;
    }

    private void SetUpChrome()
    {
        SetUpLogo();

        _pond = new Ducks.DuckPond(DuckLayer);

        DuckLayer.SizeChanged += (_, _) =>
        {
            _ducksNeedRebuild = true;
            RequestSettle();
        };
        RebuildDucks();

        _duckRest = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _duckRest.Tick += (_, _) =>
        {
            _duckRest!.Stop();
            _pond?.Start();
        };

        BtnSidebar.Click += (_, _) => ToggleSidebar();

        SidebarPanel.SizeChanged += (_, _) =>
        {
            if (!_sidebarAnimating)
            {
                SideInner.Width = SidebarPanel.ActualWidth;
            }
        };

        UpdateMonitorChip();
    }

    private void UpdateMonitorChip()
    {
        bool running = _settings.MonitorEnabled;

        MonGlyph.Stroke = ThemeTokens.Brush(
            ThemeTokens.Colour(_theme, running ? "Green" : "Gray"));

        MonLbl.Text = running ? $"{_settings.MonitorIntervalSec}s" : "fermo";
    }

    private void StartMonitor()
    {
        _monitorTimer?.Stop();
        UpdateMonitorChip();

        if (!_settings.MonitorEnabled)
        {
            return;
        }

        _monitorTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(10, _settings.MonitorIntervalSec))
        };

        _monitorTimer.Tick += async (_, _) =>
        {
            if (_session.IsRunning)
            {
                return;
            }

            IReadOnlyList<string> hosts = NoteHosts();
            if (hosts.Count == 0)
            {
                return;
            }

            await _session.RunAsync(string.Join(',', hosts), _settings.ToScanOptions(), keepExisting: true);
            RefreshCounts();
        };

        _monitorTimer.Start();
    }
}
