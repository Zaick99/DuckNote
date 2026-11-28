using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DuckNote.App.Theme;

namespace DuckNote.App.Ui;

/// <summary>
/// Una scelta del tasto destro su un avviso (Fatto, Rimanda di un promemoria): fa la sua cosa e
/// l'avviso se ne va. Gli avvisi non hanno pulsanti: il clic porta all'evento.
/// </summary>
public sealed record ToastAction(string Label, Action Run);

/// <summary>
/// Notifiche non bloccanti in basso a destra (al posto dei popup di conferma per gli esiti
/// positivi): entrano scivolando, restano qualche secondo (si fermano col mouse sopra).
/// </summary>
public static class Toast
{
    private static Panel? _host;

    public static void Attach(Panel host) => _host = host;

    public static void Success(string title, string text, Action? open = null, string? tab = null) => Show("ChipGreen", "Green", "", title, text, open, tab: tab);
    public static void Info(string title, string text, Action? open = null, string? tab = null) => Show("ChipBlue", "Blue", "", title, text, open, tab: tab);
    public static void Warning(string title, string text, Action? open = null, string? tab = null) => Show("ChipOrange", "Orange", "", title, text, open, tab: tab);

    public static void Error(string title, string text, Action? open = null, string? tab = null) => Show("ChipRed", "Red", "", title, text, open, tab: tab);

    /// <summary>Il tipo di avviso scritto sulla linguetta, dall'icona (o dal tono).</summary>
    public static string Kind(string glyph, string fg) => glyph switch
    {
        "" => "Promemoria",
        "" => "Calendario",
        _ => fg switch
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
    public static void Show(string chip, string fg, string glyph, string title, string text, Action? open = null, bool sticky = false, string? tab = null, IReadOnlyList<ToastAction>? more = null)
    {
        if (_host is null) return;
        var app = Application.Current;

        var icon = new Border { Style = (Style)app.FindResource(chip), Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
        var ig = new TextBlock { Text = glyph, Style = (Style)app.FindResource("Icon"), HorizontalAlignment = HorizontalAlignment.Center };
        ig.SetResourceReference(TextBlock.ForegroundProperty, fg);
        icon.Child = ig;

        var body = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        body.Children.Add(new TextBlock { Text = title, Style = (Style)app.FindResource("TxtBody"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 40 });
        body.Children.Add(new TextBlock { Text = text, Style = (Style)app.FindResource("TxtCaption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MaxHeight = 90, TextTrimming = TextTrimming.CharacterEllipsis });

        var close = new Button { Content = new TextBlock { Text = "", Style = (Style)app.FindResource("Icon"), FontSize = 9 }, Style = (Style)app.FindResource("Toolbtn"), Width = 22, Height = 22, MinWidth = 0, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Chiudi" };
        var grid = new Grid { Margin = new Thickness(12, 12, 8, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(body, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(icon);
        grid.Children.Add(body);
        grid.Children.Add(close);

        // la linguetta: il tipo e l'ora (come il nome e UP/DOWN sulle schede della mappa)
        var kind = new TextBlock { Text = tab ?? Kind(glyph, fg), FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        kind.SetResourceReference(TextBlock.ForegroundProperty, "Label");
        var time = new TextBlock { Text = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), FontSize = 9.5, FontWeight = FontWeights.Bold, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        time.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var tabBox = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 0, 0, -1),
            Padding = new Thickness(10, 3, 10, 4),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { kind, time } }
        };
        tabBox.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        tabBox.SetResourceReference(Border.BorderBrushProperty, "Separator");

        // la riga d'accento lungo il bordo di sopra del corpo, nel tono
        var accent = new Border { Height = 2, Margin = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(1), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Top };
        accent.SetResourceReference(Border.BackgroundProperty, fg);

        var face = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = new Grid { Children = { grid, accent } }
        };
        face.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        face.SetResourceReference(Border.BorderBrushProperty, "Separator");

        var card = new StackPanel
        {
            Width = 340,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { tabBox, face },
            RenderTransform = new TranslateTransform(40, 0),
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.24, Color = Colors.Black },
        };
        _host.Children.Insert(0, card);
        while (_host.Children.Count > 4) _host.Children.RemoveAt(_host.Children.Count - 1);

        card.BeginAnimation(UIElement.OpacityProperty, Motion.Slide(0, 1, 240, Fx.Expo()));
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Motion.Slide(60, 0, 700, Fx.Spring(6)));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(open is null ? 5 : 8) };
        var gone = false;
        void Dismiss()
        {
            if (gone) return;
            gone = true;
            timer.Stop();
            var fade = Motion.Slide(card.Opacity, 0, 180, Motion.Ease(mode: System.Windows.Media.Animation.EasingMode.EaseIn));
            fade.Completed += (_, _) => _host?.Children.Remove(card);
            card.BeginAnimation(UIElement.OpacityProperty, fade);
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Motion.Slide(0, 30, 180, null));
        }

        // un clic sull'avviso porta all'evento (non sulla X, che chiude e basta)
        if (open is not null)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (Inside(close, e.OriginalSource)) return;
                e.Handled = true;
                open();
                Dismiss();
            };
        }

        // le scelte in piu' (Fatto, Rimanda) col tasto destro: nessun pulsante sull'avviso
        if (more is { Count: > 0 })
        {
            var menu = new ContextMenu();
            foreach (var choice in more)
            {
                var item = new MenuItem { Header = choice.Label };
                item.Click += (_, _) =>
                {
                    choice.Run();
                    Dismiss();
                };
                menu.Items.Add(item);
            }
            card.ContextMenu = menu;
        }

        timer.Tick += (_, _) => Dismiss();
        if (!sticky)
        {
            card.MouseEnter += (_, _) => timer.Stop();
            card.MouseLeave += (_, _) => { if (!gone) timer.Start(); };
            timer.Start();
        }
        close.Click += (_, _) => Dismiss();
    }

    private static bool Inside(DependencyObject part, object? source)
    {
        for (var at = source as DependencyObject; at is not null; at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, part)) return true;
        }

        return false;
    }
}
