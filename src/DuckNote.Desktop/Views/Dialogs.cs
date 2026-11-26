using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Charts;
using DuckNote.Notes.Links;

namespace DuckNote.Desktop.Views;

/// <summary>La base dei dialoghi di DuckNote: una scheda col titolo, un corpo e i pulsanti in fondo.</summary>
internal static class Dialog
{
    public static Window Make(string title, double width)
    {
        Window window = new()
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontSize = 12.5
        };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("BgElevated"));
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
                e.Handled = true;
            }
        };
        return window;
    }

    public static ControlTheme? Theme(string key) => Themes.Of(key);

    public static TextBlock Heading(string text) => Tinted(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.Bold }, "Label");

    public static TextBlock Caption(string text) =>
        Tinted(new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) }, "LabelTertiary");

    public static TextBlock Label(string text) =>
        Tinted(new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 14, 0, 5) }, "LabelTertiary");

    public static T Tinted<T>(T block, string token) where T : TextBlock
    {
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(token));
        return block;
    }

    public static Button Button(string text, string theme, Action click)
    {
        Button button = new() { Content = text, Theme = Theme(theme), MinWidth = 90 };
        button.Click += (_, _) => click();
        return button;
    }

    public static StackPanel Buttons(params Control[] buttons)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 20, 0, 0) };
        row.Children.AddRange(buttons);
        return row;
    }
}

/// <summary>Messaggi e domande sì/no.</summary>
public static class Message
{
    /// <summary>Una finestra con un messaggio sola (quando la finestra principale non c'e').</summary>
    public static Window Window(string title, string text, Action closed)
    {
        Window window = Dialog.Make(title, 420);
        window.ShowInTaskbar = true;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children =
            {
                Dialog.Heading(title),
                Dialog.Caption(text),
                Dialog.Buttons(Dialog.Button("Chiudi", "Primary", window.Close))
            }
        };
        window.Closed += (_, _) => closed();
        return window;
    }

    public static async Task<bool> Ask(Window? owner, string title, string text, string confirm, bool danger = false)
    {
        bool answer = false;
        Window window = Dialog.Make(title, 420);
        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children =
            {
                Dialog.Heading(title),
                Dialog.Caption(text),
                Dialog.Buttons(
                    Dialog.Button("Annulla", "Quiet", window.Close),
                    Dialog.Button(confirm, danger ? "Danger" : "Primary", () => { answer = true; window.Close(); }))
            }
        };

        if (owner is { IsVisible: true })
        {
            await window.ShowDialog(owner);
        }
        else
        {
            TaskCompletionSource closed = new();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await closed.Task;
        }

        return answer;
    }

    public static async Task Tell(Window? owner, string title, string text)
    {
        Window window = Dialog.Make(title, 420);
        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children = { Dialog.Heading(title), Dialog.Caption(text), Dialog.Buttons(Dialog.Button("Va bene", "Primary", window.Close)) }
        };

        if (owner is { IsVisible: true })
        {
            await window.ShowDialog(owner);
        }
    }
}

/// <summary>Cosa ha scelto la finestra del collegamento.</summary>
public sealed record LinkChoice(string Address, string Text, bool Removed);

/// <summary>
/// Il collegamento: il testo da un lato, l'indirizzo dall'altro, e il modo di toglierlo che
/// lascia il testo dov'e'. Si accettano solo http, https e mailto.
/// </summary>
public static class LinkDialog
{
    public static async Task<LinkChoice?> Ask(Window? owner, string shown, string address)
    {
        if (owner is null)
        {
            return null;
        }

        LinkChoice? choice = null;
        bool editing = address.Length > 0;
        Window window = Dialog.Make(editing ? "Modifica il collegamento" : "Aggiungi un collegamento", 460);

        TextBox text = new() { Text = shown, PlaceholderText = "Il testo da mostrare nella nota", IsReadOnly = shown.Length > 0 };
        TextBox url = new() { Text = address, PlaceholderText = "https://example.com oppure nome@example.com" };
        TextBlock problem = Dialog.Tinted(new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 6, 0, 0) }, "Red");

        void Save()
        {
            if (!text.IsReadOnly && (text.Text ?? string.Empty).Trim().Length == 0)
            {
                problem.Text = "Scrivi il testo da mostrare nella nota.";
                problem.IsVisible = true;
                text.Focus();
                return;
            }

            if (WebAddress.Parse(url.Text) is not { } parsed)
            {
                problem.Text = (url.Text ?? string.Empty).Trim().Length == 0
                    ? "Scrivi un indirizzo, oppure togli il collegamento."
                    : "Indirizzo non valido: si accettano http, https e mailto.";
                problem.IsVisible = true;
                url.Classes.Add("error");
                url.Focus();
                return;
            }

            choice = new LinkChoice(parsed.ToString(), (text.Text ?? string.Empty).Trim(), Removed: false);
            window.Close();
        }

        url.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Save();
                e.Handled = true;
            }
        };

        List<Control> buttons = [];

        if (editing)
        {
            Button remove = Dialog.Button("Togli il collegamento", "Quiet", () =>
            {
                choice = new LinkChoice(string.Empty, shown, Removed: true);
                window.Close();
            });
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            buttons.Add(remove);
        }

        buttons.Add(Dialog.Button("Annulla", "Quiet", window.Close));
        buttons.Add(Dialog.Button("Salva", "Primary", Save));

        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children =
            {
                Dialog.Heading(window.Title ?? string.Empty),
                Dialog.Caption(text.IsReadOnly ? "Il collegamento si appoggia al testo scelto." : "Niente di selezionato: scrivi il testo da mostrare e il suo indirizzo."),
                Dialog.Label("TESTO"), text,
                Dialog.Label("INDIRIZZO"), url,
                problem,
                Dialog.Caption(OperatingSystem.IsMacOS()
                    ? "Senza schema viene aggiunto https://. Nella nota si apre con ⌘+clic."
                    : "Senza schema viene aggiunto https://. Nella nota si apre con Ctrl+clic."),
                Dialog.Buttons([.. buttons])
            }
        };

        window.Opened += (_, _) => (text.IsReadOnly ? url : text).Focus();
        await window.ShowDialog(owner);
        return choice;
    }
}

/// <summary>Il grafico: si scrive come testo (tipo, titolo, una riga per valore) e si vede subito.</summary>
public static class ChartDialog
{
    public static async Task<string?> Ask(Window? owner, string text)
    {
        if (owner is null)
        {
            return null;
        }

        string? result = null;
        Window window = Dialog.Make("Grafico", 720);
        TextBox spec = new()
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 220,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10, 8),
            FontFamily = Application.Current?.TryFindResource("Mono", out object? mono) == true && mono is FontFamily family ? family : FontFamily.Default
        };
        ChartPreview preview = new() { Spec = ChartSpec.Read(text), Height = 220 };
        spec.TextChanged += (_, _) => preview.Spec = ChartSpec.Read(spec.Text);

        Border previewCard = new() { Classes = { "well" }, Child = preview, Padding = new Thickness(10) };
        Grid body = new() { ColumnDefinitions = new ColumnDefinitions("*,16,*"), Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetColumn(previewCard, 2);
        body.Children.Add(spec);
        body.Children.Add(previewCard);

        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children =
            {
                Dialog.Heading("Grafico"),
                Dialog.Caption("tipo: barre, linea o torta · titolo: il nome · poi una riga per valore: etichetta e numero"),
                body,
                Dialog.Buttons(
                    Dialog.Button("Annulla", "Quiet", window.Close),
                    Dialog.Button("Salva il grafico", "Primary", () => { result = spec.Text ?? string.Empty; window.Close(); }))
            }
        };

        window.Opened += (_, _) => spec.Focus();
        await window.ShowDialog(owner);
        return result;
    }
}

/// <summary>L'anteprima del grafico nel dialogo: barre, linea o torta, coi colori del tema.</summary>
public sealed class ChartPreview : Control
{
    private static readonly string[] Series = ["Blue", "Green", "Orange", "Purple", "Teal", "Pink", "Yellow"];
    private ChartSpec _spec = ChartSpec.Read(string.Empty);

    public ChartSpec Spec
    {
        get => _spec;
        set
        {
            _spec = value;
            InvalidateVisual();
        }
    }

    private IBrush Ink(string token) => this.TryFindResource(token, ActualThemeVariant, out object? brush) && brush is IBrush ink ? ink : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        double top = _spec.Title.Length > 0 ? 28 : 8;

        if (_spec.Title.Length > 0)
        {
            FormattedText title = new(_spec.Title, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12.5, Ink("Label"));
            context.DrawText(title, new Point((w - title.Width) / 2, 4));
        }

        IReadOnlyList<ChartPoint> points = _spec.Points;

        if (points.Count == 0)
        {
            FormattedText empty = new("Nessun valore", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Ink("LabelTertiary"));
            context.DrawText(empty, new Point((w - empty.Width) / 2, h / 2 - 8));
            return;
        }

        if (_spec.Shape == ChartShape.Pie)
        {
            double total = Math.Max(0.0001, _spec.Total);
            double r = Math.Max(10, Math.Min((h - top - 8) / 2, w / 4));
            Point centre = new(r + 10, top + (h - top) / 2);
            double angle = -Math.PI / 2;

            for (int i = 0; i < points.Count; i++)
            {
                double slice = Math.Max(0, points[i].Value) / total * Math.PI * 2;
                StreamGeometry piece = new();

                using (StreamGeometryContext g = piece.Open())
                {
                    g.BeginFigure(centre, true);
                    g.LineTo(new Point(centre.X + r * Math.Cos(angle), centre.Y + r * Math.Sin(angle)));
                    g.ArcTo(new Point(centre.X + r * Math.Cos(angle + slice), centre.Y + r * Math.Sin(angle + slice)), new Size(r, r), 0, slice > Math.PI, SweepDirection.Clockwise);
                    g.EndFigure(true);
                }

                context.DrawGeometry(Ink(Series[i % Series.Length]), null, piece);
                FormattedText label = new($"{points[i].Label} {points[i].Value}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Ink("LabelSecondary"));
                context.FillRectangle(Ink(Series[i % Series.Length]), new Rect(centre.X + r + 18, top + 8 + i * 18, 10, 10), 2);
                context.DrawText(label, new Point(centre.X + r + 34, top + 5 + i * 18));
                angle += slice;
            }

            return;
        }

        double tallest = Math.Max(0.0001, _spec.Tallest);
        double plotH = h - top - 22;
        double step = (w - 20) / points.Count;
        context.DrawLine(new Pen(Ink("Separator")), new Point(10, top + plotH), new Point(w - 10, top + plotH));
        Point? previous = null;

        for (int i = 0; i < points.Count; i++)
        {
            double value = Math.Max(0, points[i].Value) / tallest * (plotH - 10);
            double cx = 10 + step * (i + 0.5);

            if (_spec.Shape == ChartShape.Line)
            {
                Point here = new(cx, top + plotH - value);

                if (previous is { } before)
                {
                    context.DrawLine(new Pen(Ink("Blue"), 2.2), before, here);
                }

                context.DrawEllipse(Ink("Blue"), null, here, 3.5, 3.5);
                previous = here;
            }
            else
            {
                double bw = Math.Max(6, step * 0.62);
                context.FillRectangle(Ink(Series[i % Series.Length]), new Rect(cx - bw / 2, top + plotH - value, bw, value), 3);
            }

            FormattedText label = new(points[i].Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10.5, Ink("LabelSecondary"));
            context.DrawText(label, new Point(cx - label.Width / 2, top + plotH + 4));
        }
    }
}
