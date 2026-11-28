using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DuckNote.App.Scanning;
using DuckNote.Notes.Groups;

namespace DuckNote.App.Pages;

/// <summary>
/// I gruppi della pagina Rete, a sinistra della tabella, come le voci del menu laterale: una
/// riga per gruppo con la freccia, la tessera colorata, il nome, le pillole degli host spenti
/// (rosse) e attivi (verdi) e l'interruttore Ping (il monitoraggio li controlla o no). La riga
/// scelta e' evidenziata come la pagina aperta nel menu; aperto, un gruppo mostra rientrati i suoi
/// host (un clic apre la scheda) e i suoi comandi. Un clic sceglie il gruppo: la tabella mostra i
/// suoi host, e i filtri e la ricerca lavorano dentro di lui. In testa Tutti, poi la Nota, i
/// gruppi propri e la Ricerca.
/// </summary>
public partial class NetworkPage
{
    private readonly Dictionary<string, GroupRow> _groupRows = [];
    private readonly HashSet<string> _expanded = [];
    private string _groupOrder = string.Empty;

    /// <summary>Quanti host di un gruppo aperto si elencano sotto di lui.</summary>
    private const int MembersShown = 8;

    /// <summary>
    /// Il filtro di stato: lo tiene <see cref="_state"/>, preso dal pulsante che si accende, non
    /// chiesto ai pulsanti (uno appena acceso e uno non ancora spento darebbero il filtro vecchio).
    /// </summary>
    private void OnState(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true, Tag: string tag } || !Enum.TryParse(tag, out HostState state) || state == _state)
        {
            return;
        }

        _state = state;

        // parte anche durante InitializeComponent (radio preselezionato): la tabella non c'e' ancora
        if (Table is not null && _ws is not null)
        {
            ShowSession();
        }
    }

    private void OnNewGroup(object sender, RoutedEventArgs e) => EditGroup(null);

    private void OnCloseCard(object sender, RoutedEventArgs e)
    {
        Table.SelectedItem = null;
        Show(null);
    }

    private void OnPageSize(object sender, SizeChangedEventArgs e) => Dock(e.NewSize.Width);

    /// <summary>
    /// La scheda dell'host accanto alla tabella se resta posto per lei (almeno 560 punti),
    /// altrimenti sopra, a destra, come la scheda di un apparato sulla mappa.
    /// </summary>
    private void Dock(double width)
    {
        bool docked = width - 250 - 14 - 14 - 340 >= 560;
        if (docked == _docked)
        {
            return;
        }

        _docked = docked;
        CardGap.Width = new GridLength(docked ? 14 : 0);
        CardColumn.Width = new GridLength(docked ? 340 : 0);
        Grid.SetColumn(HostCard, docked ? 4 : 2);
        HostCard.Width = docked ? double.NaN : 340;
        HostCard.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        HostCard.Margin = docked ? new Thickness(0) : new Thickness(0, 8, 8, 44);
        Panel.SetZIndex(HostCard, docked ? 0 : 5);
        HostCard.Effect = docked ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.18, Direction = 270 };
        HostCard.Visibility = docked || _shown is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Le righe dei gruppi, coi numeri di adesso: si rifanno solo se i gruppi cambiano.</summary>
    private void FillGroups()
    {
        IReadOnlyList<HostRow> rows = [.. _ws.Session.Rows.Where(row => _query.Matches(row.Facts()))];
        IReadOnlyList<GroupView> views = GroupViews.Build(rows, _ws.Groups, _ws.Settings.MonitorEnabled, _ws.GroupFlags);

        int own = _ws.Groups.All.Count;
        GroupCount.Text = own == 0 ? string.Empty : own == 1 ? "1 gruppo" : string.Create(CultureInfo.CurrentCulture, $"{own} gruppi");

        string order = string.Join('|', views.Select(view => view.Id));
        if (order != _groupOrder)
        {
            _groupOrder = order;
            GroupList.Children.Clear();

            foreach (GroupView view in views)
            {
                if (!_groupRows.TryGetValue(view.Id, out GroupRow? row))
                {
                    row = new GroupRow(this, view.Id);
                    _groupRows[view.Id] = row;
                }

                GroupList.Children.Add(row);
            }

            foreach (string gone in _groupRows.Keys.Except(views.Select(view => view.Id)).ToList())
            {
                _groupRows.Remove(gone);
            }
        }

        bool locked = _ws.IsLocked;
        foreach (GroupView view in views)
        {
            bool open = _expanded.Contains(view.Id);
            IReadOnlyList<HostRow> members = open
                ? [.. rows.Where(row => GroupViews.InGroup(view.Id, row, _ws.Groups) && GroupViews.Matches(_state, row))]
                : [];
            _groupRows[view.Id].Show(view, view.Id == _group, open, members, _ws.Settings.MonitorEnabled, locked);
        }
    }

    private void PickGroup(string id)
    {
        if (_group == id)
        {
            return;
        }

        _group = id;
        ShowGroup();
        ShowSession();

        // la scheda di un host che il gruppo nuovo non ha si chiude (non salta a un altro host)
        if (_shown is not null && !_visible.Contains(_shown))
        {
            Table.SelectedItem = null;
            Show(null);
        }
    }

    private void ToggleExpanded(string id)
    {
        if (!_expanded.Remove(id))
        {
            _expanded.Add(id);
        }

        FillGroups();
    }

    private void SetGroupMonitor(string id, bool on)
    {
        _ws.SetMonitor(id, on);
        string name = id switch
        {
            GroupViews.All => "Il monitoraggio",
            ScanGroups.Note => "Il monitoraggio della nota",
            GroupViews.Found => "Il monitoraggio della ricerca",
            _ => $"Il monitoraggio di «{_ws.Groups.Find(id)?.Name}»"
        };
        _ws.Say(on ? $"{name} e' acceso: i suoi host si controllano da soli." : $"{name} e' spento.");
        ShowSession();
    }

    private void PickMember(HostRow row)
    {
        if (!_visible.Contains(row))
        {
            ShowAll.IsChecked = true;
        }

        Reveal(row.Shown.Length > 0 ? row.Shown : row.IP);
    }

    private async Task CheckGroup(string id)
    {
        switch (id)
        {
            case GroupViews.All:
                await _ws.ScanGroup(ScanGroups.Note);
                foreach (ScanGroup group in _ws.Groups.All.Where(group => group.Targets.Count > 0))
                {
                    await _ws.ScanGroup(group.Id);
                }
                break;
            case GroupViews.Found:
                if (RangeBox.Text.Trim() is { Length: > 0 } range)
                {
                    await _ws.ScanRange(range);
                }
                break;
            default:
                await _ws.ScanGroup(id);
                break;
        }
    }

    private static string IconOf(string icon) => icon switch
    {
        "network" => "",
        "note" => "",
        "search" => "",
        _ => ""
    };

    /// <summary>La riga di un gruppo (e, aperta, i suoi host): aggiornata sul posto, niente si rifa' a ogni giro.</summary>
    private sealed class GroupRow : StackPanel
    {
        private readonly NetworkPage _page;
        private readonly string _id;
        private readonly Button _row;
        private readonly TextBlock _chevron = new() { Text = "", FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _chevronHit;
        private readonly Border _tile = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _icon = new() { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _name = new() { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Border _down;
        private readonly Border _up;
        private readonly Border _none;
        private readonly CheckBox _ping;
        private readonly Grid _detail = new() { Margin = new Thickness(18, 1, 6, 6), Visibility = Visibility.Collapsed };
        private readonly StackPanel _members = new();
        private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        private bool _updating;
        private string _tone = string.Empty;

        public GroupRow(NetworkPage page, string id)
        {
            _page = page;
            _id = id;

            _chevron.SetResourceReference(StyleProperty, "Icon");
            _chevron.FontSize = 9;
            _chevron.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
            _chevronHit = new Border { Child = _chevron, Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(2, 6, 6, 6), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Mostra o nascondi i suoi host" };
            _chevronHit.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _page.ToggleExpanded(_id);
            };

            _icon.SetResourceReference(StyleProperty, "TileGlyph");
            _tile.Child = _icon;

            _down = Badge("RedSoft", "Red");
            _up = Badge("GreenSoft", "Green");
            _none = Badge("BgFieldAlt", "LabelSecondary");

            _ping = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            _ping.SetResourceReference(StyleProperty, "Switch");
            _ping.Click += (_, e) =>
            {
                e.Handled = true;
                if (!_updating)
                {
                    _page.SetGroupMonitor(_id, _ping.IsChecked == true);
                }
            };

            StackPanel badges = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _down, _up, _none } };

            Grid line = new();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Put(line, _chevronHit, 0);
            Put(line, _tile, 1);
            Put(line, _name, 2);
            Put(line, badges, 3);
            Put(line, _ping, 4);

            // la riga del menu laterale: alta 36, angoli da 8, scelta come la pagina aperta
            _row = new Button { Content = line, Margin = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 0, 8, 0) };
            _row.SetResourceReference(StyleProperty, "NavItem");
            _row.Margin = new Thickness(0, 0, 0, 1);
            _row.Click += (_, _) => _page.PickGroup(_id);

            // aperto: i suoi host sotto, rientrati, con la riga guida come i titoli sotto un foglio
            Rectangle rail = new() { Width = 1, Margin = new Thickness(0, 2, 12, 2) };
            rail.SetResourceReference(Shape.FillProperty, "Separator");
            _detail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _detail.Children.Add(rail);
            StackPanel inside = new() { Children = { _members, _actions } };
            Put(_detail, inside, 1);

            Children.Add(_row);
            Children.Add(_detail);
        }

        private static void Put(Grid grid, UIElement child, int column)
        {
            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        private Border Badge(string background, string foreground)
        {
            TextBlock text = new() { FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
            Border badge = new() { Child = text, Visibility = Visibility.Collapsed, Margin = new Thickness(4, 0, 0, 0) };
            badge.SetResourceReference(StyleProperty, "Badge");
            badge.SetResourceReference(Border.BackgroundProperty, background);
            return badge;
        }

        private static void Count(Border badge, int count)
        {
            badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)badge.Child).Text = count > 999 ? "999+" : count.ToString(CultureInfo.CurrentCulture);
        }

        public void Show(GroupView view, bool selected, bool open, IReadOnlyList<HostRow> members, bool monitorAll, bool locked)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;

            if (_tone != view.Tone)
            {
                _tone = view.Tone;
                _tile.SetResourceReference(Border.BackgroundProperty, view.Tone);
            }

            _row.Tag = selected ? "Active" : null;
            _name.Text = view.Name;
            _icon.Text = IconOf(view.Icon);

            Count(_down, view.Down);
            Count(_up, view.Up);
            Count(_none, view.Up == 0 && view.Down == 0 ? view.Total : 0);

            List<string> parts = [view.Total == 1 ? "1 host" : string.Create(culture, $"{view.Total} host")];
            if (view.Up > 0) parts.Add(string.Create(culture, $"{view.Up} attivi"));
            if (view.Down > 0) parts.Add(view.Down == 1 ? "1 spento" : string.Create(culture, $"{view.Down} spenti"));
            if (view.Wait > 0) parts.Add(string.Create(culture, $"{view.Wait} da controllare"));
            _row.ToolTip = $"{string.Join(" · ", parts)}\n{view.Hint}";

            _updating = true;
            _ping.IsChecked = view.Monitor;
            _updating = false;
            _ping.IsEnabled = !locked && (view.Id == GroupViews.All || monitorAll);
            _ping.ToolTip = view.Id == GroupViews.All
                ? (view.Monitor ? "Ping: il monitoraggio e' acceso. Spegnilo per fermare tutti i controlli" : "Ping: il monitoraggio e' spento. Accendilo per controllare gli host da soli")
                : !monitorAll ? "Il monitoraggio e' spento: accendilo da Tutti"
                : view.Monitor ? "Ping: il monitoraggio controlla questi host. Spegni per lasciarli stare" : "Ping: questi host si controllano solo a mano. Accendi per farli controllare dal monitoraggio";

            _chevron.Text = open ? "" : "";
            _detail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

            if (open)
            {
                ShowMembers(members);
                ShowActions(view, locked);
            }
        }

        private void ShowMembers(IReadOnlyList<HostRow> members)
        {
            _members.Children.Clear();

            if (members.Count == 0)
            {
                TextBlock none = new() { Text = "Nessun host con questi filtri.", FontSize = 11.5, Margin = new Thickness(4, 4, 0, 4) };
                none.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
                _members.Children.Add(none);
                return;
            }

            foreach (HostRow row in members.Take(MembersShown))
            {
                Ellipse dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                dot.SetResourceReference(Shape.FillProperty, row.Tone);
                TextBlock name = new() { Text = row.Name.Length > 0 ? row.Name : row.IP, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                name.SetResourceReference(TextBlock.ForegroundProperty, "LabelSecondary");
                TextBlock side = new() { Text = row.IsUp ? row.RoundTripShort : string.Empty, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                side.SetResourceReference(TextBlock.FontFamilyProperty, "FMono");
                side.SetResourceReference(TextBlock.ForegroundProperty, "LabelQuaternary");

                Grid line = new();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Put(line, dot, 0);
                Put(line, name, 1);
                Put(line, side, 2);

                Button item = new() { Content = line, Height = 26, Padding = new Thickness(6, 0, 6, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = $"{row.IP} · {row.ToneText}" };
                item.SetResourceReference(StyleProperty, "Toolbtn");
                item.Height = 26;
                item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                HostRow picked = row;
                item.Click += (_, _) => _page.PickMember(picked);
                _members.Children.Add(item);
            }

            if (members.Count > MembersShown)
            {
                TextBlock words = new() { Text = string.Create(CultureInfo.CurrentCulture, $"e altri {members.Count - MembersShown}…"), FontSize = 11.5 };
                words.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                Button more = new() { Content = words, Height = 24, Padding = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Mostra tutto il gruppo nella tabella" };
                more.SetResourceReference(StyleProperty, "Toolbtn");
                more.Height = 24;
                more.Click += (_, _) => _page.PickGroup(_id);
                _members.Children.Add(more);
            }
        }

        private void ShowActions(GroupView view, bool locked)
        {
            _actions.Children.Clear();

            Button check = Link("", view.Id switch
            {
                GroupViews.All => "Controlla tutto",
                ScanGroups.Note => "Controlla la nota",
                GroupViews.Found => "Riprova l'intervallo",
                _ => "Analizza"
            });
            check.IsEnabled = !locked && !_page._ws.Session.IsRunning && (!view.Editable || view.Targets.Count > 0);
            check.ToolTip = view.Editable ? (view.Targets.Count == 0 ? "Il gruppo e' vuoto: aggiungi reti o indirizzi con Modifica" : $"Analizza {string.Join(", ", view.Targets)}") : view.Hint;
            check.Click += async (_, _) => await _page.CheckGroup(_id);
            _actions.Children.Add(check);

            if (view.Editable)
            {
                Button edit = Link("", "Modifica");
                edit.IsEnabled = !locked;
                edit.Click += (_, _) =>
                {
                    if (_page._ws.Groups.Find(_id) is { } group)
                    {
                        _page.EditGroup(group);
                    }
                };
                _actions.Children.Add(edit);
            }
        }

        private static Button Link(string icon, string text)
        {
            TextBlock glyph = new() { Text = icon, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(StyleProperty, "Icon");
            glyph.FontSize = 10;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            TextBlock words = new() { Text = text, FontSize = 11.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            words.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            Button button = new() { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, words } }, Height = 24, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 0) };
            button.SetResourceReference(StyleProperty, "Toolbtn");
            button.Height = 24;
            return button;
        }
    }
}
