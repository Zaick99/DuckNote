using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Una scelta del tasto destro su un avviso (Fatto, Rimanda di un promemoria): fa la sua cosa e
/// l'avviso se ne va. Gli avvisi non hanno pulsanti: il clic porta all'evento.
/// </summary>
public sealed record NoticeAction(string Label, Action Run);

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

    public void Success(string title, string text, Action? open = null, string? tab = null) => Toast("check", "Green", title, text, open, tab: tab);

    public void Info(string title, string text, Action? open = null, string? tab = null) => Toast("info", "Blue", title, text, open, tab: tab);

    public void Warning(string title, string text, Action? open = null, string? tab = null) => Toast("warning", "Orange", title, text, open, tab: tab);

    public void Error(string title, string text, Action? open = null, string? tab = null) => Toast("error", "Red", title, text, open, tab: tab);

    /// <summary>Il tipo di avviso scritto sulla linguetta, dall'icona (o dal tono).</summary>
    public static string Kind(string icon, string tone) => icon switch
    {
        "bell" => "Promemoria",
        "calendar" => "Calendario",
        "check" => "Fatto",
        "warning" => "Attenzione",
        "error" => "Errore",
        _ => tone switch
        {
            "Green" => "Fatto",
            "Orange" => "Attenzione",
            "Red" => "Errore",
            _ => "Avviso"
        }
    };

    /// <summary>
    /// Un avviso, nella forma delle schede della mappa: una linguetta col tipo (<paramref name="tab"/>,
    /// o quello dell'icona) e l'ora, e sotto il corpo con la riga d'accento nel tono, la tessera
    /// dell'icona, il titolo e il testo. Niente pulsanti: un clic sull'avviso porta all'evento
    /// (<paramref name="open"/>) e lo chiude, la X lo chiude soltanto. <paramref name="more"/>: le
    /// scelte del tasto destro (Fatto e Rimanda di un promemoria). <paramref name="sticky"/>: resta
    /// finche' non lo si apre o lo si chiude.
    /// </summary>
    public void Toast(string icon, string tone, string title, string text, Action? open = null, bool sticky = false, string? tab = null, IReadOnlyList<NoticeAction>? more = null)
    {
        Glyph glyph = new() { Kind = icon, Width = 15, Height = 15, Thickness = 1.7 };
        glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(tone));
        Border tile = new()
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Top,
            Child = glyph
        };
        tile.Bind(Border.BackgroundProperty, tile.GetResourceObservable(tone + "Soft"));

        StackPanel body = new() { Margin = new Thickness(12, 0, 8, 0) };
        TextBlock head = new() { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxHeight = 40 };
        head.Bind(TextBlock.ForegroundProperty, head.GetResourceObservable("Label"));
        TextBlock words = new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), MaxHeight = 90 };
        words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("LabelSecondary"));
        body.Children.Add(head);
        body.Children.Add(words);

        // la linguetta: il tipo e l'ora (come il nome e UP/DOWN sulle schede della mappa)
        TextBlock kind = new() { Text = tab ?? Kind(icon, tone), FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        kind.Bind(TextBlock.ForegroundProperty, kind.GetResourceObservable("Label"));
        TextBlock time = new() { Text = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), FontSize = 9.5, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        time.Bind(TextBlock.ForegroundProperty, time.GetResourceObservable(tone));
        Border tabBox = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 0, 0, -1),
            Padding = new Thickness(10, 3, 10, 4),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { kind, time } }
        };
        tabBox.Bind(Border.BackgroundProperty, tabBox.GetResourceObservable("BgElevated"));
        tabBox.Bind(Border.BorderBrushProperty, tabBox.GetResourceObservable("Separator"));

        // la riga d'accento lungo il bordo di sopra del corpo, nel tono
        Border accent = new() { Height = 2, Margin = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(1), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Top };
        accent.Bind(Border.BackgroundProperty, accent.GetResourceObservable(tone));

        Border face = new()
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };
        face.Bind(Border.BackgroundProperty, face.GetResourceObservable("BgElevated"));
        face.Bind(Border.BorderBrushProperty, face.GetResourceObservable("Separator"));

        StackPanel card = new()
        {
            Opacity = 0,
            Children = { tabBox, face },
            Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 6, BlurRadius = 22, Opacity = 0.2, Color = Colors.Black }
        };

        Button close = new() { Content = new Glyph { Kind = "close", Width = 9, Height = 9 }, Width = 22, Height = 22, MinWidth = 22, VerticalAlignment = VerticalAlignment.Top };
        close.Theme = Themes.Of("Tool");
        ToolTip.SetTip(close, "Chiudi");

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 12, 8, 12) };
        Grid.SetColumn(body, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(tile);
        grid.Children.Add(body);
        grid.Children.Add(close);
        face.Child = new Grid { Children = { grid, accent } };

        DispatcherTimer rest = new() { Interval = TimeSpan.FromSeconds(open is null ? 5 : 8) };

        void Done()
        {
            rest.Stop();
            Dismiss(card);
        }

        // un clic sull'avviso porta all'evento (non sulla X, che chiude e basta)
        if (open is not null)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            bool pressed = false;
            card.PointerPressed += (_, e) => pressed = e.GetCurrentPoint(card).Properties.IsLeftButtonPressed && !Inside(close, e.Source);
            card.PointerReleased += (_, e) =>
            {
                if (!pressed || Inside(close, e.Source))
                {
                    pressed = false;
                    return;
                }

                pressed = false;
                open();
                Done();
            };
        }

        // le scelte in piu' (Fatto, Rimanda) col tasto destro: nessun pulsante sull'avviso
        if (more is { Count: > 0 })
        {
            ContextMenu menu = new();

            foreach (NoticeAction choice in more)
            {
                MenuItem item = new() { Header = choice.Label };
                item.Click += (_, _) =>
                {
                    choice.Run();
                    Done();
                };
                menu.Items.Add(item);
            }

            card.ContextMenu = menu;
        }

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

        rest.Tick += (_, _) =>
        {
            if (card.IsPointerOver)
            {
                return;
            }

            Done();
        };

        if (!sticky)
        {
            rest.Start();
        }

        close.Click += (_, _) => Done();
    }

    private static bool Inside(Control part, object? source) =>
        source is Visual visual && (ReferenceEquals(visual, part) || part.IsVisualAncestorOf(visual));

    private void Dismiss(Control card)
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
