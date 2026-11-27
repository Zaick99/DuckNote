using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DuckNote.App.Theme;

namespace DuckNote.App.Ui;

/// <summary>
/// Un pulsante di un avviso: fa una cosa (e l'avviso se ne va), oppure apre un menu di scelte
/// (Rimanda: fra 10 minuti, fra un'ora, domani).
/// </summary>
public sealed record ToastAction(string Label, Action? Run, bool Primary = false, IReadOnlyList<(string Label, Action Run)>? Choices = null);

/// <summary>
/// Notifiche non bloccanti in basso a destra (al posto dei popup di conferma per gli esiti
/// positivi): entrano scivolando, restano 6 s (si fermano col mouse sopra), azione facoltativa.
/// </summary>
public static class Toast
{
    private static Panel? _host;

    public static void Attach(Panel host) => _host = host;

    public static void Success(string title, string text, string? action = null, Action? onAction = null) => Show("ChipGreen", "Green", "\uE73E", title, text, action, onAction);
    public static void Info(string title, string text, string? action = null, Action? onAction = null) => Show("ChipBlue", "Blue", "\uE946", title, text, action, onAction);
    public static void Warning(string title, string text, string? action = null, Action? onAction = null) => Show("ChipOrange", "Orange", "\uE7BA", title, text, action, onAction);

    public static void Error(string title, string text, string? action = null, Action? onAction = null) => Show("ChipRed", "Red", "\uEA39", title, text, action, onAction);

    private static void Show(string chip, string fg, string glyph, string title, string text, string? action, Action? onAction) =>
        Show(chip, fg, glyph, title, text, action is null ? [] : [new ToastAction(action, onAction)], sticky: false);

    /// <summary>
    /// Un avviso con piu' pulsanti; <paramref name="sticky"/>: resta finche' non si sceglie o non
    /// lo si chiude (i promemoria del calendario).
    /// </summary>
    public static void Show(string chip, string fg, string glyph, string title, string text, IReadOnlyList<ToastAction> actions, bool sticky)
    {
        if (_host is null) return;
        var app = Application.Current;

        var icon = new Border { Style = (Style)app.FindResource(chip), Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
        var ig = new TextBlock { Text = glyph, Style = (Style)app.FindResource("Icon"), HorizontalAlignment = HorizontalAlignment.Center };
        ig.SetResourceReference(TextBlock.ForegroundProperty, fg);
        icon.Child = ig;

        var body = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        body.Children.Add(new TextBlock { Text = title, Style = (Style)app.FindResource("TxtBody"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 40 });
        body.Children.Add(new TextBlock { Text = text, Style = (Style)app.FindResource("TxtCaption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MaxHeight = 90, TextTrimming = TextTrimming.CharacterEllipsis });

        var close = new Button { Content = new TextBlock { Text = "\uE711", Style = (Style)app.FindResource("Icon"), FontSize = 9 }, Style = (Style)app.FindResource("Toolbtn"), Width = 22, Height = 22, MinWidth = 0, VerticalAlignment = VerticalAlignment.Top };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(body, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(icon);
        grid.Children.Add(body);
        grid.Children.Add(close);
        var buttons = new List<Button>();
        if (actions.Count > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var act in actions)
            {
                var btn = new Button { Content = act.Label, Style = (Style)app.FindResource(act.Primary ? "PrimaryBtn" : "SoftBtn"), Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0) };
                if (act.Choices is { Count: > 0 } choices)
                {
                    var menu = new ContextMenu { PlacementTarget = btn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                    foreach (var (label, run) in choices)
                    {
                        var item = new MenuItem { Header = label };
                        item.Click += (_, _) => { run(); btn.Tag = "done"; btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
                        menu.Items.Add(item);
                    }
                    btn.ContextMenu = menu;
                    btn.Click += (_, _) => { if (btn.Tag is not "done") menu.IsOpen = true; };
                }
                else
                {
                    btn.Click += (_, _) => act.Run?.Invoke();
                    btn.Tag = "done";
                }
                buttons.Add(btn);
                row.Children.Add(btn);
            }
            body.Children.Add(row);
        }

        var card = new Border
        {
            Width = 340,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14, 12, 10, 12),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = grid,
            RenderTransform = new TranslateTransform(40, 0),
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.28, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        card.SetResourceReference(Border.BorderBrushProperty, "Separator");
        _host.Children.Insert(0, card);
        while (_host.Children.Count > 4) _host.Children.RemoveAt(_host.Children.Count - 1);

        card.BeginAnimation(UIElement.OpacityProperty, Motion.Slide(0, 1, 240, Fx.Expo()));
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Motion.Slide(60, 0, 700, Fx.Spring(6)));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(actions.Count == 0 ? 5 : 9) };
        void Dismiss()
        {
            timer.Stop();
            var fade = Motion.Slide(card.Opacity, 0, 180, Motion.Ease(mode: System.Windows.Media.Animation.EasingMode.EaseIn));
            fade.Completed += (_, _) => _host?.Children.Remove(card);
            card.BeginAnimation(UIElement.OpacityProperty, fade);
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Motion.Slide(0, 30, 180, null));
        }
        timer.Tick += (_, _) => Dismiss();
        if (!sticky)
        {
            card.MouseEnter += (_, _) => timer.Stop();
            card.MouseLeave += (_, _) => timer.Start();
            timer.Start();
        }
        close.Click += (_, _) => Dismiss();
        // un pulsante che ha fatto la sua cosa (o una scelta del suo menu) chiude l'avviso
        foreach (var btn in buttons) btn.Click += (_, _) => { if (btn.Tag is "done") Dismiss(); };
    }
}
