using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// I pezzi delle pagine a schede (Impostazioni, Cassaforte), come nella versione WPF: gruppi in
/// card, ogni riga col testo a sinistra e il controllo a destra, separatori sottili fra le righe.
/// </summary>
public static class Kit
{
    public static TextBlock Header(string text, bool first = false)
    {
        TextBlock header = new() { Text = text.ToUpperInvariant() };
        header.Classes.Add("section");
        header.Margin = first ? new Thickness(16, 6, 0, 8) : new Thickness(16, 20, 0, 8);
        return header;
    }

    public static Border Group(params Control[] rows)
    {
        StackPanel stack = new() { Margin = new Thickness(0, 4) };

        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                stack.Children.Add(Sep());
            }

            stack.Children.Add(rows[i]);
        }

        Border card = new() { Child = stack };
        card.Classes.Add("card");
        return card;
    }

    /// <summary>Una riga: titolo e nota a sinistra, il controllo a destra.</summary>
    public static Grid Row(string label, string? note, Control? right, string labelTone = "Label")
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 11) };
        row.Children.Add(Texts(label, note, labelTone));

        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
        }

        return row;
    }

    /// <summary>Una riga con l'icona davanti (le spiegazioni della cassaforte).</summary>
    public static Grid IconRow(string icon, string tone, string label, string note, string labelTone = "Label")
    {
        Glyph glyph = new() { Kind = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(tone));

        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(16, 11) };
        row.Children.Add(glyph);
        StackPanel texts = Texts(label, note, labelTone);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        return row;
    }

    /// <summary>Titolo e nota sopra, i controlli sotto (porte, tema).</summary>
    public static StackPanel Block(string label, string? note, params Control[] below)
    {
        StackPanel block = Texts(label, note, "Label");
        block.Margin = new Thickness(16, 12);

        foreach (Control control in below)
        {
            block.Children.Add(control);
        }

        return block;
    }

    public static StackPanel Texts(string label, string? note, string labelTone = "Label")
    {
        TextBlock title = new() { Text = label, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable(labelTone));
        StackPanel texts = new() { VerticalAlignment = VerticalAlignment.Center, Children = { title } };

        if (!string.IsNullOrEmpty(note))
        {
            texts.Children.Add(Note(note));
        }

        return texts;
    }

    public static TextBlock Note(string text)
    {
        TextBlock note = new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        note.Classes.Add("caption");
        return note;
    }

    public static Rectangle Sep()
    {
        Rectangle line = new() { Margin = new Thickness(16, 0) };
        line.Classes.Add("sep");
        return line;
    }

    public static OnOff Switch(bool on, Action<bool> changed)
    {
        OnOff toggle = new() { IsChecked = on };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    public static TextBox Number(double width, string hint) => new()
    {
        Width = width,
        TextAlignment = TextAlignment.Right,
        PlaceholderText = hint,
        Height = 32
    };

    /// <summary>Un riquadro di spiegazione col colore del suo tono (verde: cosa protegge, rosso: cosa no).</summary>
    public static Border Box(string icon, string tone, string text)
    {
        Glyph glyph = new() { Kind = icon, Width = 15, Height = 15, Margin = new Thickness(2, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(tone));
        TextBlock words = new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 19, FontSize = 12.5 };
        words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("Label"));

        Grid inside = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        inside.Children.Add(glyph);
        Grid.SetColumn(words, 1);
        inside.Children.Add(words);

        Border box = new() { Child = inside, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        box.Bind(Border.BackgroundProperty, box.GetResourceObservable(tone + "Soft"));
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable(tone + "Border"));
        return box;
    }

    /// <summary>Un'etichetta colorata (ATTIVA, APERTA).</summary>
    public static Border Chip(string text, string tone)
    {
        TextBlock words = new() { Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold };
        words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable(tone));
        Border chip = new() { Child = words, Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(9) };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable(tone + "Soft"));
        return chip;
    }

    public static Button Labelled(string theme, string? icon, string text, double height = 32)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 7 };

        if (icon is not null)
        {
            content.Children.Add(new Glyph { Kind = icon, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center });
        }

        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Theme = Themes.Of(theme), Height = height, Content = content };
    }

    /// <summary>Due colonne che non scorrono a finestra normale; sotto una certa larghezza una sola.</summary>
    public static Grid Columns(Control left, Control right)
    {
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,18,*"), Margin = new Thickness(0, 0, 4, 8) };
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    public static StackPanel Stack(params Control[] children)
    {
        StackPanel stack = new();
        stack.Children.AddRange(children);
        return stack;
    }

    public static void Shake(Control control)
    {
        TranslateTransform shift = control.RenderTransform as TranslateTransform ?? new TranslateTransform();
        control.RenderTransform = shift;
        Motion.Tween((control, "shake"), 0, 1, 420, t => shift.X = Math.Sin(t * Math.PI * 6) * 7 * (1 - t), () => shift.X = 0);
    }
}
