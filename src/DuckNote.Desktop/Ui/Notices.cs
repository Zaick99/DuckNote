using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Gli avvisi della finestra: l'ultimo evento in basso a sinistra (compare, resta il tempo di
/// leggerlo, se ne va) e le notifiche in basso a destra (esiti, errori, con un'azione se serve).
/// Sono finestrelle a parte (Popup), non pezzi della finestra: sopra al foglio della nota, che
/// e' un WebView, un pezzo della finestra non si vedrebbe.
/// </summary>
public sealed class Notices
{
    private readonly Popup _pillPopup;
    private readonly Border _pill;
    private readonly TextBlock _pillText;
    private readonly DispatcherTimer _pillRest = new();
    private DateTime _pillSince;

    private readonly Popup _toastPopup;
    private readonly StackPanel _toasts = new() { Spacing = 10, Width = 340 };

    /// <summary>Gli avvisi della finestra principale, per le pagine che hanno un esito da dire.</summary>
    public static Notices? Current { get; private set; }

    public Notices(Panel host, Control anchor)
    {
        Current = this;
        _pillText = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 600 };
        _pillText.Bind(TextBlock.ForegroundProperty, _pillText.GetResourceObservable("LabelSecondary"));
        _pill = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12, 5, 13, 6),
            BorderThickness = new Thickness(1),
            Child = _pillText,
            BoxShadow = BoxShadows.Parse("0 3 14 0 #29000000"),
            Margin = new Thickness(6, 6, 6, 12),
            IsHitTestVisible = false
        };
        _pill.Bind(Border.BackgroundProperty, _pill.GetResourceObservable("BgElevated"));
        _pill.Bind(Border.BorderBrushProperty, _pill.GetResourceObservable("Separator"));

        _pillPopup = Corner(anchor, PopupAnchor.BottomLeft, PopupGravity.TopRight, 16, -6, _pill);
        _toastPopup = Corner(anchor, PopupAnchor.BottomRight, PopupGravity.TopLeft, -10, -6, new Border { Padding = new Thickness(8, 8, 8, 12), Child = _toasts });

        host.Children.Add(_pillPopup);
        host.Children.Add(_toastPopup);

        _pillRest.Tick += (_, _) => HideStatus();
    }

    private static Popup Corner(Control anchor, PopupAnchor corner, PopupGravity gravity, double x, double y, Control child) => new()
    {
        PlacementTarget = anchor,
        Placement = PlacementMode.AnchorAndGravity,
        PlacementAnchor = corner,
        PlacementGravity = gravity,
        HorizontalOffset = x,
        VerticalOffset = y,
        IsLightDismissEnabled = false,
        Child = child
    };

    /// <summary>Le finestrelle si chiudono (finestra ridotta a icona, nota bloccata).</summary>
    public void Close()
    {
        _pillPopup.IsOpen = false;
        _toastPopup.IsOpen = false;
        _toasts.Children.Clear();
    }

    // ------------------------------------------------------------------ l'ultimo evento

    public void Status(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            HideStatus();
            return;
        }

        _pillText.Text = text;
        _pillSince = DateTime.UtcNow;
        _pillPopup.IsOpen = true;
        Motion.Show(_pill, 8, 240);

        _pillRest.Stop();
        _pillRest.Interval = TimeSpan.FromMilliseconds(Math.Clamp(2200 + text.Length * 45, 3200, 9000));
        _pillRest.Start();
    }

    public void HideStatus()
    {
        _pillRest.Stop();
        Motion.Hide(_pill, 8, 160, () => _pillPopup.IsOpen = false);
    }

    /// <summary>Si riprende a scrivere: l'avviso, se lo si e' avuto davanti abbastanza, lascia libero il foglio.</summary>
    public void Typing()
    {
        if (_pillPopup.IsOpen && (DateTime.UtcNow - _pillSince).TotalMilliseconds > 1500)
        {
            HideStatus();
        }
    }

    // ------------------------------------------------------------------ notifiche

    public void Success(string title, string text) => Toast("check", "Green", title, text);

    public void Info(string title, string text) => Toast("info", "Blue", title, text);

    public void Warning(string title, string text) => Toast("warning", "Orange", title, text);

    public void Error(string title, string text) => Toast("error", "Red", title, text);

    public void Toast(string icon, string tone, string title, string text, string? action = null, Action? onAction = null)
    {
        Glyph glyph = new() { Kind = icon, Width = 15, Height = 15, Thickness = 1.7 };
        glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(tone));
        Border chip = new()
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            VerticalAlignment = VerticalAlignment.Top,
            Child = glyph
        };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable(tone + "Soft"));

        StackPanel body = new() { Margin = new Thickness(12, 0, 8, 0) };
        TextBlock head = new() { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 };
        head.Bind(TextBlock.ForegroundProperty, head.GetResourceObservable("Label"));
        TextBlock words = new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), MaxHeight = 90 };
        words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("LabelSecondary"));
        body.Children.Add(head);
        body.Children.Add(words);

        Border card = new()
        {
            Padding = new Thickness(14, 12, 10, 12),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #33000000"),
            Opacity = 0
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("BgElevated"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Separator"));

        Button close = new() { Content = new Glyph { Kind = "close", Width = 9, Height = 9 }, Width = 22, Height = 22, MinWidth = 22, VerticalAlignment = VerticalAlignment.Top };
        close.Theme = Themes.Of("Tool");

        if (action is not null)
        {
            Button act = new() { Content = action, Height = 26, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            act.Theme = Themes.Of("Soft");
            act.Click += (_, _) =>
            {
                onAction?.Invoke();
                Dismiss(card);
            };
            body.Children.Add(act);
        }

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(body, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(chip);
        grid.Children.Add(body);
        grid.Children.Add(close);
        card.Child = grid;

        _toasts.Children.Insert(0, card);

        while (_toasts.Children.Count > 3)
        {
            _toasts.Children.RemoveAt(_toasts.Children.Count - 1);
        }

        _toastPopup.IsOpen = true;
        TranslateTransform shift = new(40, 0);
        card.RenderTransform = shift;
        Motion.Tween((card, "in"), 0, 1, 320, t =>
        {
            card.Opacity = t;
            shift.X = 40 * (1 - t);
        });

        DispatcherTimer rest = new() { Interval = TimeSpan.FromSeconds(action is null ? 5 : 9) };
        rest.Tick += (_, _) =>
        {
            if (card.IsPointerOver)
            {
                return;
            }

            rest.Stop();
            Dismiss(card);
        };
        rest.Start();
        close.Click += (_, _) =>
        {
            rest.Stop();
            Dismiss(card);
        };
    }

    private void Dismiss(Border card)
    {
        TranslateTransform shift = card.RenderTransform as TranslateTransform ?? new TranslateTransform();
        card.RenderTransform = shift;
        Motion.Tween((card, "in"), card.Opacity, 0, 200, t =>
        {
            card.Opacity = t;
            shift.X = 30 * (1 - t);
        }, () =>
        {
            _toasts.Children.Remove(card);

            if (_toasts.Children.Count == 0)
            {
                _toastPopup.IsOpen = false;
            }
        });
    }
}
