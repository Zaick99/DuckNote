using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DuckNote.Desktop.Notes;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Outline;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// L'indice dei fogli, nella sidebar: ogni foglio con la sua tessera colorata e, sotto, i suoi
/// titoli H1 e H2 (che si aprono e chiudono), la ricerca nei fogli, un foglio nuovo, il nome che
/// si cambia con un doppio clic. Le righe le mette in fila <see cref="OutlineTree"/>, lo stesso
/// della versione WPF.
/// </summary>
public sealed partial class NotesPage
{
    private const double Step = 14;

    private static readonly string[] TileTokens = ["Orange", "Blue", "Green", "Pink", "Purple", "Teal", "Red", "Indigo", "Yellow"];

    private readonly DockPanel _navigator = new();
    private readonly StackPanel _rows = new() { Margin = new Thickness(10, 0, 10, 6) };
    private readonly TextBox _filter = new() { PlaceholderText = "Cerca nei fogli", Height = 30, MinHeight = 30, Margin = new Thickness(12, 4, 12, 6), IsVisible = false };
    private readonly TextBlock _count = new() { FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ToggleButton _findSheets = new() { Width = 26, Height = 26 };
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);
    private string _shown = string.Empty;
    private string? _renaming;

    /// <summary>L'indice dei fogli: la finestra lo mette al centro della sidebar.</summary>
    public Control Navigator => _navigator;

    private void BuildOutline()
    {
        Grid head = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(20, 10, 12, 2) };
        TextBlock title = new() { Text = "Fogli", FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("LabelTertiary"));
        _count.Bind(TextBlock.ForegroundProperty, _count.GetResourceObservable("LabelQuaternary"));

        _findSheets.Theme = Find("ToolToggle");
        _findSheets.Content = new Glyph { Kind = "search", Width = 12, Height = 12 };
        ToolTip.SetTip(_findSheets, OperatingSystem.IsMacOS() ? "Cerca nei fogli (⌘⇧F)" : "Cerca nei fogli (Ctrl+Maiusc+F)");
        _findSheets.Click += (_, _) => ShowFilter(_findSheets.IsChecked == true);

        Button add = new() { Width = 26, Height = 26, Theme = Find("Tool"), Content = new Glyph { Kind = "add", Width = 12, Height = 12 }, Margin = new Thickness(2, 0, 0, 0) };
        ToolTip.SetTip(add, "Nuovo foglio");
        add.Click += (_, _) => AddSheet();

        Grid.SetColumn(_count, 1);
        Grid.SetColumn(_findSheets, 2);
        Grid.SetColumn(add, 3);
        head.Children.Add(title);
        head.Children.Add(_count);
        head.Children.Add(_findSheets);
        head.Children.Add(add);

        _filter.TextChanged += (_, _) => RefreshOutline();
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ShowFilter(false);
                FocusText();
                e.Handled = true;
            }
        };

        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(_filter, Dock.Top);
        _navigator.Children.Add(head);
        _navigator.Children.Add(_filter);
        _navigator.Children.Add(new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
    }

    public void ShowFilter(bool on)
    {
        _findSheets.IsChecked = on;

        if (on)
        {
            SidebarWanted?.Invoke();
            _filter.IsVisible = true;
            _filter.Focus();
            _filter.SelectAll();
            return;
        }

        _filter.Text = string.Empty;
        _filter.IsVisible = false;
    }

    /// <summary>Rifa l'indice, se e' cambiato qualcosa (si chiama a ogni modifica del foglio).</summary>
    private void RefreshOutline()
    {
        string wanted = _filter.Text ?? string.Empty;
        IReadOnlyList<OutlineRow> rows = _ws.IsLocked ? [] : OutlineTree.Build(_ws.Book.Sheets.Select(sheet => sheet.Outline()), _front ? _sheet?.Id : null, wanted, _open);

        int pages = _ws.Book.Sheets.Count;
        string many = pages == 1 ? "1 foglio" : string.Create(CultureInfo.CurrentCulture, $"{pages} fogli");
        _count.Text = rows.Count == 0 && wanted.Length > 0 ? $"{many}, niente che corrisponda" : many;

        StringBuilder signature = new();

        foreach (OutlineRow row in rows)
        {
            signature.Append(row.Key).Append('|').Append(row.Title).Append('|').Append(row.IsOpen).Append(row.IsCurrent)
                .Append(row.Hits).Append(row.Depth).Append(row.IsLast).Append(row.HasChildren).Append('\n');
        }

        signature.Append(_renaming).Append(_front);

        if (signature.ToString() == _shown)
        {
            return;
        }

        _shown = signature.ToString();
        _rows.Children.Clear();

        foreach (OutlineRow row in rows)
        {
            _rows.Children.Add(Row(row));
        }
    }

    private Control Row(OutlineRow row)
    {
        double height = row.IsPage ? 30 : 26;
        Grid line = new() { Height = height, ColumnDefinitions = new ColumnDefinitions("Auto,14,*,Auto"), Background = Brushes.Transparent };

        // i binari dell'albero
        Avalonia.Controls.Shapes.Path rails = new() { Width = Math.Max(0.1, row.Depth * Step), Data = Geometry.Parse(Rails(row, height)), StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Stretch };
        rails.Bind(Shape.StrokeProperty, rails.GetResourceObservable("Separator"));
        line.Children.Add(rails);

        if (row.HasChildren)
        {
            Glyph mark = new() { Kind = row.IsOpen ? "down" : "right", Width = 9, Height = 9, Thickness = 1.8 };
            mark.Bind(Glyph.ForegroundProperty, mark.GetResourceObservable("LabelTertiary"));
            Border chevron = new() { Child = mark, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(2, 4) };
            chevron.PointerPressed += (_, e) =>
            {
                _open[row.Key] = !row.IsOpen;
                RefreshOutline();
                e.Handled = true;
            };
            Grid.SetColumn(chevron, 1);
            line.Children.Add(chevron);
        }

        StackPanel label = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (row.IsPage)
        {
            Border tile = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            tile.Bind(Border.BackgroundProperty, tile.GetResourceObservable(TileTokens[Stable(row.PageId) % TileTokens.Length]));
            tile.Child = new Image { Source = SheetIcon, Width = 17, Height = 17 };
            label.Children.Add(tile);
        }
        else
        {
            Border level = new() { Width = 22, Height = 15, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            level.Bind(Border.BackgroundProperty, level.GetResourceObservable("AccentSoft"));
            TextBlock code = new() { Text = row.Level == 1 ? "H1" : "H2", FontSize = 9, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            code.Bind(TextBlock.ForegroundProperty, code.GetResourceObservable("Accent"));
            level.Child = code;
            label.Children.Add(level);
        }

        Control name;

        if (row.IsPage && _renaming == row.PageId)
        {
            TextBox box = new() { Text = _ws.Book.ById(row.PageId)?.Name ?? string.Empty, PlaceholderText = "Nome del foglio", Height = 24, MinHeight = 24, FontSize = 12, Width = 150, Padding = new Thickness(6, 0) };
            box.AttachedToVisualTree += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    StopRenaming(row.PageId, e.Key == Key.Enter ? box.Text : null);
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => StopRenaming(row.PageId, box.Text);
            name = box;
        }
        else
        {
            TextBlock text = new()
            {
                Text = row.Title,
                FontSize = row.IsPage ? 13 : 12.5,
                FontWeight = row.IsPage ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(row.IsPage || row.IsCurrent ? "Label" : "LabelSecondary"));
            name = text;
        }

        label.Children.Add(name);
        Grid.SetColumn(label, 2);
        line.Children.Add(label);

        StackPanel right = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (row.Hits > 0)
        {
            Border badge = new() { Classes = { "badge" } };
            badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable("AccentSoft"));
            TextBlock hits = new() { Text = row.Hits.ToString(CultureInfo.CurrentCulture) };
            hits.Bind(TextBlock.ForegroundProperty, hits.GetResourceObservable("Accent"));
            badge.Child = hits;
            right.Children.Add(badge);
        }

        if (row.IsCurrent)
        {
            Ellipse here = new() { Width = 6, Height = 6, Margin = new Thickness(6, 0, 4, 0) };
            here.Bind(Shape.FillProperty, here.GetResourceObservable("Accent"));
            right.Children.Add(here);
        }

        Grid.SetColumn(right, 3);
        line.Children.Add(right);

        Button item = new() { Content = line, Theme = Find("MenuRow"), Padding = new Thickness(6, 0), Margin = new Thickness(0, 0, 0, 1), HorizontalContentAlignment = HorizontalAlignment.Stretch };

        if (row.IsCurrent && row.IsPage && _front)
        {
            item.Classes.Add("current");
            item.Bind(BackgroundProperty, item.GetResourceObservable("AccentSoft"));
        }

        item.Click += (_, _) => OpenRow(row);
        item.DoubleTapped += (_, _) =>
        {
            if (row.IsPage)
            {
                StartRenaming(row.PageId);
            }
        };

        if (row.IsPage)
        {
            ContextMenu menu = new();
            MenuItem add = new() { Header = "Nuovo foglio", Icon = new Glyph { Kind = "add" } };
            add.Click += (_, _) => AddSheet();
            MenuItem rename = new() { Header = "Cambia nome", Icon = new Glyph { Kind = "note" } };
            rename.Click += (_, _) => StartRenaming(row.PageId);
            MenuItem drop = new() { Header = "Elimina foglio", Icon = new Glyph { Kind = "trash" }, IsEnabled = _ws.Book.Sheets.Count > 1 };
            drop.Click += (_, _) => DropSheet(row.PageId);
            menu.Items.Add(add);
            menu.Items.Add(rename);
            menu.Items.Add(new Separator());
            menu.Items.Add(drop);
            item.ContextMenu = menu;
        }

        return item;
    }

    private static readonly Lazy<Avalonia.Media.Imaging.Bitmap?> SheetBitmap = new(() =>
    {
        try
        {
            return new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://DuckNote/Assets/sheet.png")));
        }
        catch (Exception problem) when (problem is IOException or FileNotFoundException)
        {
            return null;
        }
    });

    private static Avalonia.Media.Imaging.Bitmap? SheetIcon => SheetBitmap.Value;

    /// <summary>Un foglio porta in cima a se'; un titolo porta alla sua riga, scorrendo.</summary>
    private void OpenRow(OutlineRow row)
    {
        if (_ws.Book.ById(row.PageId) is not { } sheet)
        {
            return;
        }

        Wanted?.Invoke();

        if (!ReferenceEquals(sheet, _sheet))
        {
            Show(sheet);
        }

        if (!row.IsPage && row.HeadingIndex >= 0)
        {
            Call($"dn.heading({row.HeadingIndex})");
        }
        else
        {
            FocusText();
        }
    }

    /// <summary>Un foglio nell'editor; quello di prima e' gia' stato tenuto a ogni modifica.</summary>
    private void Show(Sheet sheet)
    {
        Load(sheet);
        RefreshOutline();
    }

    private void AddSheet()
    {
        if (_ws.IsLocked)
        {
            return;
        }

        Sheet sheet = _ws.Book.Add();
        _ws.Touch();
        Wanted?.Invoke();
        Show(sheet);
        GiveKeyboard();
        Call("dn.focusTitle()");
        _ws.Say(string.Create(CultureInfo.CurrentCulture, $"Foglio nuovo: scrivi il titolo, oppure doppio clic sul nome nella sidebar. {_ws.Book.Sheets.Count} fogli."));
    }

    private void StartRenaming(string id)
    {
        _renaming = id;
        RefreshOutline();
    }

    private void StopRenaming(string id, string? wanted)
    {
        if (_renaming != id)
        {
            return;
        }

        _renaming = null;

        if (wanted is not null && _ws.Book.ById(id) is { } sheet)
        {
            string clean = PageIds.Clean(wanted);

            if (clean != sheet.Name)
            {
                sheet.Name = clean;
                _ws.Touch();
                _ws.Say(clean.Length > 0 ? $"Titolo: {clean}." : $"Titolo tolto: il foglio torna \"{PageIds.Untitled}\".");

                if (ReferenceEquals(sheet, _sheet))
                {
                    Call($"dn.title({Js(clean)})");
                }
            }
        }

        RefreshOutline();
        FocusText();
    }

    private async void DropSheet(string id)
    {
        if (_ws.Book.ById(id) is not { } sheet)
        {
            return;
        }

        if (_ws.Book.Sheets.Count < 2)
        {
            _ws.Say("L'ultimo foglio non si toglie.");
            return;
        }

        bool sure = await Views.Message.Ask(TopLevel.GetTopLevel(this) as Window, "Eliminare il foglio?",
            $"\"{sheet.Title}\" e tutto quello che c'e' scritto se ne vanno dalla nota.", "Elimina", danger: true);

        if (!sure)
        {
            return;
        }

        int at = _ws.Book.IndexOf(sheet);
        _ws.Book.Remove(sheet);
        _ws.Touch();
        _ws.SyncHosts();

        if (ReferenceEquals(sheet, _sheet))
        {
            Show(_ws.Book.Sheets[Math.Min(at, _ws.Book.Sheets.Count - 1)]);
        }

        RefreshOutline();
        _ws.Say($"Foglio \"{sheet.Title}\" eliminato.");
    }

    private static int Stable(string text)
    {
        uint hash = 2166136261;

        foreach (char ch in text)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return (int)(hash % int.MaxValue);
    }

    /// <summary>I binari: una verticale per ogni ramo che continua sotto, un gomito verso questa riga.</summary>
    private static string Rails(OutlineRow row, double height)
    {
        double half = 6;
        double middle = Math.Round(height / 2);
        StringBuilder path = new();

        for (int level = 1; level < row.Depth; level++)
        {
            if (row.Continues[level])
            {
                path.Append(CultureInfo.InvariantCulture, $"M {(level - 1) * Step + half} 0 V {height} ");
            }
        }

        if (row.Depth > 0)
        {
            double x = (row.Depth - 1) * Step + half;
            path.Append(CultureInfo.InvariantCulture, $"M {x} 0 V {middle} H {row.Depth * Step - 2} ");

            if (!row.IsLast)
            {
                path.Append(CultureInfo.InvariantCulture, $"M {x} {middle} V {height} ");
            }
        }

        return path.Length == 0 ? "M 0 0" : path.ToString().TrimEnd();
    }
}
