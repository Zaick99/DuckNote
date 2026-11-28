using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using DuckNote.App.Scanning;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Groups;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// I gruppi della pagina Rete, a sinistra della tabella, come le voci del menu laterale: una
/// riga per gruppo con la freccia, la tessera colorata, il nome, le pillole degli host spenti
/// (rosse) e attivi (verdi) e l'interruttore Ping (il monitoraggio li controlla o no). La riga
/// scelta e' evidenziata come la pagina aperta nel menu; aperto, un gruppo mostra rientrati i suoi
/// host (un clic apre la scheda) e i suoi comandi, come i titoli sotto un foglio. Un clic sceglie
/// il gruppo: la tabella mostra i suoi host, e i filtri e la ricerca lavorano dentro di lui. In
/// testa Tutti, poi la Nota, i gruppi propri e la Ricerca.
/// </summary>
public sealed partial class NetworkPage
{
    private readonly StackPanel _groupList = new() { Spacing = 1, Margin = new Thickness(0, 0, 4, 12) };
    private readonly Dictionary<string, GroupCard> _groupCards = [];
    private readonly HashSet<string> _expanded = [];
    private readonly TextBlock _groupCount = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private string _groupOrder = string.Empty;

    /// <summary>Quanti host di un gruppo aperto si elencano sotto di lui.</summary>
    private const int MembersShown = 8;

    private Control GroupsPanel()
    {
        // la testata come quella dei fogli: il nome piccolo, quanti sono, il "+"
        TextBlock title = new() { Text = "Gruppi", FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("LabelTertiary"));
        _groupCount.Bind(TextBlock.ForegroundProperty, _groupCount.GetResourceObservable("LabelQuaternary"));

        Button add = new() { Theme = Themes.Of("Tool"), Width = 26, Height = 26, MinWidth = 26, Content = Icon("add", 12) };
        ToolTip.SetTip(add, "Nuovo gruppo: reti, intervalli e indirizzi da analizzare e controllare insieme");
        add.Click += async (_, _) => await EditGroup(null);

        Grid head = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(10, 4, 4, 4) };
        head.Children.Add(title);
        head.Children.Add(Cell(_groupCount, 1));
        head.Children.Add(Cell(add, 2));

        ScrollViewer scroll = new()
        {
            Content = _groupList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        Grid panel = new() { RowDefinitions = new RowDefinitions("Auto,*") };
        panel.Children.Add(head);
        panel.Children.Add(Row(scroll, 1));
        return panel;
    }

    /// <summary>Le schede dei gruppi, coi numeri di adesso: si rifanno solo se i gruppi cambiano.</summary>
    private void FillGroups()
    {
        IReadOnlyList<HostRow> rows = [.. _ws.Session.Rows.Where(row => _query.Matches(row.Facts()))];
        IReadOnlyList<GroupView> views = GroupViews.Build(rows, _ws.Groups, _ws.Settings.MonitorEnabled, _ws.GroupFlags);

        int own = _ws.Groups.All.Count;
        _groupCount.Text = own == 0 ? string.Empty : own == 1 ? "1 gruppo" : string.Create(CultureInfo.CurrentCulture, $"{own} gruppi");

        string order = string.Join('|', views.Select(view => view.Id));
        if (order != _groupOrder)
        {
            _groupOrder = order;
            _groupList.Children.Clear();

            foreach (GroupView view in views)
            {
                if (!_groupCards.TryGetValue(view.Id, out GroupCard? card))
                {
                    card = new GroupCard(this, view.Id);
                    _groupCards[view.Id] = card;
                }

                _groupList.Children.Add(card);
            }

            foreach (string gone in _groupCards.Keys.Except(views.Select(view => view.Id)).ToList())
            {
                _groupCards.Remove(gone);
            }
        }

        bool locked = _ws.IsLocked;
        foreach (GroupView view in views)
        {
            bool open = _expanded.Contains(view.Id);
            IReadOnlyList<HostRow> members = open
                ? [.. rows.Where(row => GroupViews.InGroup(view.Id, row, _ws.Groups) && GroupViews.Matches(_state, row))]
                : [];
            _groupCards[view.Id].Show(view, view.Id == _group, open, members, _ws.Settings.MonitorEnabled, locked);
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
            CloseCard();
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
        Later();
    }

    private void PickMember(HostRow row)
    {
        if (!_visible.Contains(row))
        {
            _all.IsChecked = true;
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
                if (_range.Text is { Length: > 0 } range)
                {
                    await _ws.ScanRange(range.Trim());
                }
                break;
            default:
                await _ws.ScanGroup(id);
                break;
        }
    }

    /// <summary>La riga di un gruppo (e, aperta, i suoi host): aggiornata sul posto, niente si rifa' a ogni giro.</summary>
    private sealed class GroupCard : StackPanel
    {
        private readonly NetworkPage _page;
        private readonly string _id;
        private readonly Button _row;
        private readonly Glyph _chevron = new() { Kind = "right", Width = 9, Height = 9, Thickness = 1.8 };
        private readonly Border _chevronHit;
        private readonly Border _tile = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        private readonly Glyph _icon = new() { Width = 14, Height = 14, Foreground = Brushes.White };
        private readonly TextBlock _name = new() { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Border _down = Badge("RedSoft", "Red");
        private readonly Border _up = Badge("GreenSoft", "Green");
        private readonly Border _none = Badge("BgFieldAlt", "LabelSecondary");
        private readonly OnOff _ping;
        private readonly Grid _detail = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(18, 1, 6, 6), IsVisible = false };
        private readonly StackPanel _members = new() { Spacing = 0 };
        private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        private bool _updating;
        private string _tone = string.Empty;

        public GroupCard(NetworkPage page, string id)
        {
            _page = page;
            _id = id;

            _chevron.Bind(Glyph.ForegroundProperty, _chevron.GetResourceObservable("LabelTertiary"));
            _chevronHit = new Border { Child = _chevron, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(2, 6, 6, 6), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(_chevronHit, "Mostra o nascondi i suoi host");
            _chevronHit.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _page.ToggleExpanded(_id);
            };

            _tile.Child = _icon;

            _ping = Kit.Switch(false, on =>
            {
                if (!_updating)
                {
                    _page.SetGroupMonitor(_id, on);
                }
            });
            _ping.VerticalAlignment = VerticalAlignment.Center;
            _ping.Margin = new Thickness(6, 0, 0, 0);
            // il clic dell'interruttore non risale alla riga: accende o spegne, non sceglie il gruppo
            _ping.Click += (_, e) => e.Handled = true;

            StackPanel badges = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _down, _up, _none } };

            Grid line = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
            line.Children.Add(_chevronHit);
            line.Children.Add(Cell(_tile, 1));
            line.Children.Add(Cell(_name, 2));
            line.Children.Add(Cell(badges, 3));
            line.Children.Add(Cell(_ping, 4));

            // la riga del menu laterale: alta 36, angoli da 8, scelta come la pagina aperta
            _row = new Button { Theme = Themes.Of("Nav"), Content = line, Margin = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 0, 8, 0) };
            _row.Click += (_, _) => _page.PickGroup(_id);

            // aperto: i suoi host sotto, rientrati, con la riga guida come i titoli sotto un foglio
            Border rail = new() { Width = 1, Margin = new Thickness(0, 2, 12, 2) };
            rail.Bind(Border.BackgroundProperty, rail.GetResourceObservable("Separator"));
            _detail.Children.Add(rail);
            _detail.Children.Add(Cell(new StackPanel { Children = { _members, _actions } }, 1));

            Children.Add(_row);
            Children.Add(_detail);
        }

        private static Border Badge(string background, string foreground)
        {
            TextBlock text = new();
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(foreground));
            Border badge = new() { Classes = { "badge" }, Child = text, IsVisible = false };
            badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable(background));
            return badge;
        }

        private static void Count(Border badge, int count)
        {
            badge.IsVisible = count > 0;
            ((TextBlock)badge.Child!).Text = count > 999 ? "999+" : count.ToString(CultureInfo.CurrentCulture);
        }

        public void Show(GroupView view, bool selected, bool open, IReadOnlyList<HostRow> members, bool monitorAll, bool locked)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;

            if (_tone != view.Tone)
            {
                _tone = view.Tone;
                _tile.Bind(Border.BackgroundProperty, _tile.GetResourceObservable(view.Tone));
            }

            _row.Classes.Set("active", selected);
            _name.Text = view.Name;
            _icon.Kind = view.Icon;

            Count(_down, view.Down);
            Count(_up, view.Up);
            // nessuno acceso o spento: il numero degli host, grigio
            Count(_none, view.Up == 0 && view.Down == 0 ? view.Total : 0);
            _none.IsVisible = view.Up == 0 && view.Down == 0;

            List<string> parts = [view.Total == 1 ? "1 host" : string.Create(culture, $"{view.Total} host")];
            if (view.Up > 0) parts.Add(string.Create(culture, $"{view.Up} attivi"));
            if (view.Down > 0) parts.Add(view.Down == 1 ? "1 spento" : string.Create(culture, $"{view.Down} spenti"));
            if (view.Wait > 0) parts.Add(string.Create(culture, $"{view.Wait} da controllare"));
            ToolTip.SetTip(_row, $"{string.Join(" · ", parts)}\n{view.Hint}");

            // l'interruttore: Tutti e' il monitoraggio intero; gli altri contano solo se e' acceso
            _updating = true;
            _ping.IsChecked = view.Monitor;
            _updating = false;
            _ping.IsEnabled = !locked && (view.Id == GroupViews.All || monitorAll);
            ToolTip.SetTip(_ping, view.Id == GroupViews.All
                ? (view.Monitor ? "Ping: il monitoraggio e' acceso. Spegnilo per fermare tutti i controlli" : "Ping: il monitoraggio e' spento. Accendilo per controllare gli host da soli")
                : !monitorAll ? "Il monitoraggio e' spento: accendilo da Tutti"
                : view.Monitor ? "Ping: il monitoraggio controlla questi host. Spegni per lasciarli stare" : "Ping: questi host si controllano solo a mano. Accendi per farli controllare dal monitoraggio");

            _chevron.Kind = open ? "down" : "right";
            _detail.IsVisible = open;

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
                none.Bind(TextBlock.ForegroundProperty, none.GetResourceObservable("LabelTertiary"));
                _members.Children.Add(none);
                return;
            }

            foreach (HostRow row in members.Take(MembersShown))
            {
                Ellipse dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                dot.Bind(Shape.FillProperty, dot.GetResourceObservable(row.Tone));
                TextBlock name = new() { Text = row.Name.Length > 0 ? row.Name : row.IP, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("LabelSecondary"));
                TextBlock side = new() { Text = row.IsUp ? row.RoundTripShort : string.Empty, FontSize = 10.5, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                side.Bind(TextBlock.ForegroundProperty, side.GetResourceObservable("LabelQuaternary"));

                Grid line = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                line.Children.Add(dot);
                line.Children.Add(Cell(name, 1));
                line.Children.Add(Cell(side, 2));

                Button item = new() { Theme = Themes.Of("MenuRow"), Content = line, Height = 26, Padding = new Thickness(6, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(item, $"{row.IP} · {row.ToneText}");
                HostRow picked = row;
                item.Click += (_, _) => _page.PickMember(picked);
                _members.Children.Add(item);
            }

            if (members.Count > MembersShown)
            {
                Button more = new() { Theme = Themes.Of("MenuRow"), Height = 24, Padding = new Thickness(6, 0), Content = new TextBlock { Text = string.Create(CultureInfo.CurrentCulture, $"e altri {members.Count - MembersShown}…"), FontSize = 11.5 } };
                ((TextBlock)more.Content).Bind(TextBlock.ForegroundProperty, ((TextBlock)more.Content).GetResourceObservable("Accent"));
                ToolTip.SetTip(more, "Mostra tutto il gruppo nella tabella");
                more.Click += (_, _) => _page.PickGroup(_id);
                _members.Children.Add(more);
            }
        }

        private void ShowActions(GroupView view, bool locked)
        {
            _actions.Children.Clear();

            Button check = Link("refresh", view.Id switch
            {
                GroupViews.All => "Controlla tutto",
                ScanGroups.Note => "Controlla la nota",
                GroupViews.Found => "Riprova l'intervallo",
                _ => "Analizza"
            });
            check.IsEnabled = !locked && !_page._ws.Session.IsRunning && (!view.Editable || view.Targets.Count > 0);
            ToolTip.SetTip(check, view.Editable ? (view.Targets.Count == 0 ? "Il gruppo e' vuoto: aggiungi reti o indirizzi con Modifica" : $"Analizza {string.Join(", ", view.Targets)}") : view.Hint);
            check.Click += async (_, _) => await _page.CheckGroup(_id);
            _actions.Children.Add(check);

            if (view.Editable)
            {
                Button edit = Link("edit", "Modifica");
                edit.IsEnabled = !locked;
                edit.Click += async (_, _) =>
                {
                    if (_page._ws.Groups.Find(_id) is { } group)
                    {
                        await _page.EditGroup(group);
                    }
                };
                _actions.Children.Add(edit);
            }
        }

        private static Button Link(string icon, string text)
        {
            Glyph glyph = new() { Kind = icon, Width = 10, Height = 10 };
            glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable("Accent"));
            TextBlock words = new() { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("Accent"));
            return new Button
            {
                Theme = Themes.Of("MenuRow"),
                Height = 24,
                Padding = new Thickness(6, 0),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { glyph, words } }
            };
        }
    }
}
