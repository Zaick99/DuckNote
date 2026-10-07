using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Models;
using DuckNote.App.Theme;

namespace DuckNote.App;

public partial class MainWindow
{
    private readonly ObservableCollection<SideItem> _sideItems = [];
    private readonly ObservableCollection<SideNode> _sideNodes = [];

    private readonly Dictionary<string, bool> _openBranches = [];

    private bool _outlineMode;

    private ContextMenu _pageMenu = null!;

    private void SetUpSidebar()
    {
        SideModeHost.Checked += (_, _) => SwitchSideMode(outline: false);
        SideModeOutline.Checked += (_, _) => SwitchSideMode(outline: true);
        SideSearch.TextChanged += (_, _) => RefreshSideList();

        SideSegHost.SizeChanged += (_, _) => MoveSideSegPill(_outlineMode ? 1 : 0, immediate: true);

        PageNew.Click += (_, _) => AddPage();
        _pageMenu = PageMenu();

        SwitchSideMode(outline: false, immediate: true);
    }

    private void JumpTo(Paragraph row)
    {
        TabNote.IsChecked = true;

        if (Editor.Template?.FindName("PART_ContentHost", Editor) is not ScrollViewer view)
        {
            return;
        }

        Editor.UpdateLayout();

        Rect spot = row.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        double target = Math.Max(0, view.VerticalOffset + spot.Top - HeadingAir);

        Motion.ScrollTo(view, target);
        Editor.Focus();
    }

    private const double HeadingAir = 60;

    private void SwitchSideMode(bool outline, bool immediate = false)
    {
        _outlineMode = outline;

        SideSearchHint.Text = outline ? "Cerca in tutte le pagine" : "Filtra host";
        SideList.ItemTemplate = (DataTemplate)FindResource(outline ? "BranchRow" : "HostRow");
        SideList.ItemContainerStyle = (Style)FindResource(outline ? "BranchItem" : "SideItem");
        SideList.ItemsSource = outline ? _sideNodes : _sideItems;
        SideList.ContextMenu = outline ? _pageMenu : HostMenu;
        PageNew.Visibility = outline ? Visibility.Visible : Visibility.Collapsed;

        MoveSideSegPill(outline ? 1 : 0, immediate);
        RefreshSideList();

        Motion.Enter(SideList, SideListT, from: outline ? 16 : -16);
    }

    private void MoveSideSegPill(int index, bool immediate = false)
    {
        if (SideSegHost.ActualWidth <= 1)
        {
            return;
        }

        double half = SideSegHost.ActualWidth / 2;
        SideSegPill.Width = half;
        SideSegPillS.CenterX = half / 2;
        Motion.MovePill(SideSegPillT, SideSegPillS, index * half, immediate);
    }

    private void RefreshSideList()
    {
        if (_formatter is null || _book is null)
        {
            return;
        }

        if (_outlineMode)
        {
            List<SideNode> shown = Outline.Flat(
                Outline.Build(_book, _page, SideSearch.Text, _openBranches));

            SyncNodes(shown);
            UpdateSideFoot(shown.Count);
            return;
        }

        List<SideItem> wanted = BuildHosts();
        SyncSideItems(wanted);
        UpdateSideFoot(wanted.Count);
    }

    private ContextMenu PageMenu()
    {
        MenuItem fresh = new() { Header = "Nuova pagina" };
        fresh.Click += (_, _) => AddPage();

        MenuItem rename = new() { Header = "Rinomina pagina" };
        rename.Click += (_, _) => RenamePage();

        MenuItem drop = new() { Header = "Elimina pagina" };
        drop.Click += (_, _) => DropPage();

        ContextMenu menu = new();
        menu.Items.Add(fresh);
        menu.Items.Add(rename);
        menu.Items.Add(drop);

        return menu;
    }

    private void OnBranchToggle(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SideNode node })
        {
            return;
        }

        e.Handled = true;

        if (!node.IsOpen)
        {
            _openBranches[node.Key] = true;
            RefreshSideList();
            return;
        }

        Fold(node);
    }

    private void Fold(SideNode node)
    {
        ListBoxItem[] rows =
        [
            .. Outline.Flat(node.Children)
                .Select(child => SideList.ItemContainerGenerator.ContainerFromItem(child))
                .OfType<ListBoxItem>()
        ];

        if (rows.Length == 0)
        {
            Shut(node);
            return;
        }

        int left = rows.Length;

        foreach (ListBoxItem row in rows)
        {
            DoubleAnimation fade = Motion.Slide(1, 0, 130, Motion.Ease(mode: EasingMode.EaseIn));

            fade.Completed += (_, _) =>
            {
                row.BeginAnimation(OpacityProperty, null);

                if (--left == 0)
                {
                    Shut(node);
                }
            };

            row.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void Shut(SideNode node)
    {
        _openBranches[node.Key] = false;
        RefreshSideList();
    }

    private void OpenBranch(SideNode node)
    {
        if (_book.ById(node.PageId) is not { } page)
        {
            return;
        }

        if (!ReferenceEquals(page, _page))
        {
            ShowPage(page);
        }

        Paragraph? where = node.Heading is { Parent: not null } heading
            ? heading
            : LiveFormatter.RowsOf(page.Document).FirstOrDefault();

        if (where is null)
        {
            TabNote.IsChecked = true;
            Editor.Focus();
            return;
        }

        if (node.IsPage)
        {
            Editor.CaretPosition = where.ContentStart;
        }

        JumpTo(where);
    }

    private void AddPage()
    {
        NotePage page = _book.Add(_editorBrushes.Text);

        _noteDirty = true;
        SideModeOutline.IsChecked = true;
        ShowPage(page);
        SaveNote();

        StatusText.Text = $"Pagina nuova: scrivi un titolo con # e si chiamera' cosi'. {_book.Count} pagine.";
        Editor.Focus();
    }

    private void RenamePage()
    {
        if (SideList.SelectedItem is not SideNode node || !node.IsPage)
        {
            StatusText.Text = "Scegli una pagina nella struttura, poi rinominala.";
            return;
        }

        foreach (SideNode other in _sideNodes)
        {
            other.EditVis = "Collapsed";
            other.LabelVis = "Visible";
        }

        node.EditVis = "Visible";
        node.LabelVis = "Collapsed";
        _renaming = node;

        Dispatcher.BeginInvoke(() => Field(node)?.Focus(), DispatcherPriority.Input);
    }

    private SideNode? _renaming;

    private void OnPageRenameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keep(sender as TextBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Forget();
            e.Handled = true;
        }
    }

    private void OnPageRenameDone(object sender, RoutedEventArgs e) => Keep(sender as TextBox);

    private void Keep(TextBox? field)
    {
        if (_renaming is null || field is null || _book.ById(_renaming.PageId) is not { } page)
        {
            Forget();
            return;
        }

        string wanted = NotePage.Clean(field.Text);

        if (wanted != page.Name)
        {
            page.Name = wanted;
            _noteDirty = true;
            SaveNote();
            StatusText.Text = wanted.Length > 0
                ? $"Pagina rinominata: {wanted}."
                : "Nome tolto: la pagina si presenta col suo primo titolo.";
        }

        Forget();
    }

    private void Forget()
    {
        if (_renaming is { } node)
        {
            node.EditVis = "Collapsed";
            node.LabelVis = "Visible";
        }

        _renaming = null;
        RefreshSideList();
    }

    private TextBox? Field(SideNode node)
    {
        if (SideList.ItemContainerGenerator.ContainerFromItem(node) is not ListBoxItem row)
        {
            return null;
        }

        row.ApplyTemplate();

        return Hunt<TextBox>(row);
    }

    private static T? Hunt<T>(DependencyObject where) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(where); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(where, i);

            if (child is T found)
            {
                return found;
            }

            if (Hunt<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private void DropPage()
    {
        NotePage? page = SideList.SelectedItem is SideNode node ? _book.ById(node.PageId) : _page;

        if (page is null)
        {
            return;
        }

        if (_book.Count == 1)
        {
            StatusText.Text = "L'ultima pagina non si toglie.";
            return;
        }

        string title = page.Title;

        if (!_book.Remove(page))
        {
            return;
        }

        _noteDirty = true;

        if (ReferenceEquals(page, _page))
        {
            ShowPage(_book.Pages[0]);
        }
        else
        {
            RefreshSideList();
        }

        SaveNote();
        StatusText.Text = $"Pagina '{title}' eliminata. Resta in note.xaml.bak fino al prossimo salvataggio.";
    }

    private List<SideItem> BuildHosts()
    {
        string filter = SideSearch.Text.Trim();
        List<SideItem> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (ScanRow row in _session.Rows)
        {
            if (row.StatusRank > 2 || !seen.Add(row.IP))
            {
                continue;
            }

            foreach (string name in (string[])[row.Hostname, row.NetBiosName, row.MdnsName])
            {
                if (name.Length > 0)
                {
                    seen.Add(name);
                }
            }

            string subtitle = row.Hostname.Length > 0 ? row.Hostname : row.NetBiosName;
            if (!Outline.Matches(filter, $"{row.IP} {subtitle}"))
            {
                continue;
            }

            items.Add(Item($"h:{row.IP}", row.IP, DotHex(row.StatusRank), subtitle, row.IP));
        }

        foreach (string host in NoteHosts())
        {
            if (!seen.Add(host) || !Outline.Matches(filter, host))
            {
                continue;
            }

            int rank = _hostStates.TryGetValue(host, out bool up) ? (up ? 0 : 2) : 4;
            items.Add(Item($"h:{host}", host, DotHex(rank), string.Empty, host));
        }

        return items;
    }

    private void SyncNodes(List<SideNode> wanted)
    {
        HashSet<string> keys = [.. wanted.Select(node => node.Key)];

        for (int i = _sideNodes.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_sideNodes[i].Key))
            {
                _sideNodes.RemoveAt(i);
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            SideNode fresh = wanted[i];

            if (i >= _sideNodes.Count || _sideNodes[i].Key != fresh.Key)
            {
                _sideNodes.Insert(i, fresh);
                continue;
            }

            SideNode kept = _sideNodes[i];
            kept.Title = fresh.Title;
            kept.Toggle = fresh.Toggle;
            kept.ToggleVis = fresh.ToggleVis;
            kept.Badge = fresh.Badge;
            kept.BadgeVis = fresh.BadgeVis;
            kept.HereVis = fresh.HereVis;
            kept.Rails = fresh.Rails;
            kept.RailWidth = fresh.RailWidth;
            kept.RowHeight = fresh.RowHeight;
            kept.Heading = fresh.Heading;
            kept.IsOpen = fresh.IsOpen;
        }

        while (_sideNodes.Count > wanted.Count)
        {
            _sideNodes.RemoveAt(_sideNodes.Count - 1);
        }
    }

    private void SyncSideItems(List<SideItem> wanted)
    {
        HashSet<string> keys = [.. wanted.Select(item => item.Key)];

        for (int i = _sideItems.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_sideItems[i].Key))
            {
                _sideItems.RemoveAt(i);
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            SideItem fresh = wanted[i];

            if (i >= _sideItems.Count || _sideItems[i].Key != fresh.Key)
            {
                _sideItems.Insert(i, fresh);
                continue;
            }

            SideItem existing = _sideItems[i];
            existing.Title = fresh.Title;
            existing.Subtitle = fresh.Subtitle;
            existing.SubVis = fresh.SubVis;
            existing.Dot = fresh.Dot;
            existing.IP = fresh.IP;
            existing.Para = fresh.Para;
        }

        while (_sideItems.Count > wanted.Count)
        {
            _sideItems.RemoveAt(_sideItems.Count - 1);
        }
    }

    private void UpdateSideFoot(int count)
    {
        if (_outlineMode)
        {
            int pages = _book?.Count ?? 0;
            string many = pages == 1 ? "1 pagina" : $"{pages} pagine";

            SideFoot.Text = count == 0 ? $"{many}, niente che corrisponda" : many;
            return;
        }

        int up = _session.UpCount;
        SideFoot.Text = count == 0 ? "Nessun host" : $"{count} host, {up} attivi";
    }

    private static SideItem Item(
        string key, string title, string dot, string subtitle, string ip, Paragraph? paragraph = null) =>
        new()
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            SubVis = subtitle.Length > 0 ? "Visible" : "Collapsed",
            Dot = dot,
            IP = ip,
            Para = paragraph
        };

    private string DotHex(int rank) => rank switch
    {
        0 => ThemeTokens.Colour(_theme, "Green"),
        1 => ThemeTokens.Colour(_theme, "Teal"),
        2 => ThemeTokens.Colour(_theme, "Red"),
        3 => ThemeTokens.Colour(_theme, "Orange"),
        _ => ThemeTokens.Colour(_theme, "LabelQuaternary")
    };
}
