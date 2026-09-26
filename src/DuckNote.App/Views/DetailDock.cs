using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DuckNote.App.Models;
using DuckNote.App.Theme;

namespace DuckNote.App.Views;

public sealed class DetailDock
{
    private const double Gap = 10;
    private const double Margin = 4;
    private const double Shift = 44;
    private const double MinimumHeight = 320;

    private readonly Window _owner;
    private readonly ResourceDictionary _resources;

    private DetailWindow? _panel;
    private ScanRow? _row;
    private int _side = 1;
    private bool _crossing;

    public DetailDock(Window owner, ResourceDictionary resources)
    {
        _owner = owner;
        _resources = resources;
    }

    public bool IsOpen { get; private set; }

    public double Width { get; set; } = 340;

    public void Show(ScanRow? row)
    {
        _row = row;
        if (IsOpen)
        {
            _panel?.Fill(row);
        }
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Open();
    }

    public void Open()
    {
        DetailWindow panel = _panel ??= Build();
        IsOpen = true;

        Rect bounds = OwnerBounds();
        (double x, int side) = Slot(bounds);
        _side = side;

        panel.Width = Width;
        panel.Height = Math.Max(MinimumHeight, bounds.Height - 8);
        panel.Top = bounds.Top + Margin;
        panel.Left = x;

        panel.Fill(_row);
        panel.DShell.Opacity = 0;
        panel.Show();
        Enter(panel);
    }

    public void Close()
    {
        if (!IsOpen || _panel is null)
        {
            return;
        }

        IsOpen = false;
        Leave(_panel, thenCross: false);
    }

    public void Follow()
    {
        if (!IsOpen || _panel is null || _crossing)
        {
            return;
        }

        Rect bounds = OwnerBounds();
        (double x, int side) = Slot(bounds);

        _panel.Top = bounds.Top + Margin;
        _panel.Height = Math.Max(MinimumHeight, bounds.Height - 8);

        if (side != _side)
        {
            _crossing = true;
            Leave(_panel, thenCross: true);
            return;
        }

        _panel.Left = x;
    }

    public void Dispose()
    {
        _panel?.Close();
        _panel = null;
        IsOpen = false;
    }

    private DetailWindow Build()
    {
        DetailWindow panel = new(_resources) { Owner = _owner };
        panel.Dismissed += (_, _) => Close();
        panel.SizeChanged += (_, _) =>
        {
            if (IsOpen && !_crossing)
            {
                Width = panel.ActualWidth;
            }
        };
        return panel;
    }

    private Rect OwnerBounds()
    {
        if (_owner.WindowState != WindowState.Maximized)
        {
            return new Rect(_owner.Left, _owner.Top, _owner.ActualWidth, _owner.ActualHeight);
        }

        Rect work = SystemParameters.WorkArea;
        return new Rect(work.Left, work.Top, work.Width, work.Height);
    }

    private (double X, int Side) Slot(Rect bounds)
    {
        Rect work = SystemParameters.WorkArea;

        double right = bounds.Left + bounds.Width + Gap;
        if (right + Width <= work.Right - Margin)
        {
            return (right, 1);
        }

        double left = bounds.Left - Gap - Width;
        if (left >= work.Left + Margin)
        {
            return (left, -1);
        }

        return (Math.Max(work.Left + Margin, work.Right - Width - 8), 1);
    }

    private void Enter(DetailWindow panel)
    {
        panel.DSlide.BeginAnimation(TranslateTransform.XProperty, null);
        panel.DShell.BeginAnimation(UIElement.OpacityProperty, null);

        double from = -Shift * _side;
        panel.DSlide.X = from;
        panel.DShell.Opacity = 0;

        panel.DSlide.BeginAnimation(
            TranslateTransform.XProperty,
            Motion.Slide(from, 0, 340, Motion.Ease("Quint")));
        panel.DShell.BeginAnimation(
            UIElement.OpacityProperty,
            Motion.Slide(0, 1, 260, Motion.Ease()));
    }

    private void Leave(DetailWindow panel, bool thenCross)
    {
        DoubleAnimation fade = Motion.Slide(panel.DShell.Opacity, 0, 200, Motion.Ease(mode: EasingMode.EaseIn));
        fade.Completed += (_, _) => Landed(panel, thenCross);

        panel.DSlide.BeginAnimation(
            TranslateTransform.XProperty,
            Motion.Slide(panel.DSlide.X, -Shift * _side, 220, Motion.Ease(mode: EasingMode.EaseIn)));
        panel.DShell.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Landed(DetailWindow panel, bool thenCross)
    {
        panel.DSlide.BeginAnimation(TranslateTransform.XProperty, null);
        panel.DShell.BeginAnimation(UIElement.OpacityProperty, null);
        panel.DShell.Opacity = 0;

        if (!thenCross)
        {
            panel.Hide();
            panel.DSlide.X = 0;
            return;
        }

        _crossing = false;
        (double x, int side) = Slot(OwnerBounds());
        panel.Left = x;
        _side = side;
        Enter(panel);
    }
}
