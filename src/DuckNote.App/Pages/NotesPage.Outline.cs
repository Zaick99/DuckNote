using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DuckNote.App.Dialogs;
using DuckNote.App.Editor;
using DuckNote.App.Theme;
using DuckNote.Notes.Notebook;
using DuckNote.Notes.Outline;

namespace DuckNote.App.Pages;

public partial class NotesPage
{
    private readonly ObservableCollection<OutlineItem> _outline = [];
    private readonly Dictionary<string, bool> _openBranches = [];
    private OutlineItem? _renaming;
    private bool _outlineBound;

    private const double HeadingAir = 60;

    /// <summary>Ricostruisce la struttura tenendo le righe che c'erano: niente sfarfallio mentre si scrive.</summary>
    private void RefreshOutline()
    {
        if (!_outlineBound)
        {
            OutlineList.ItemsSource = _outline;
            _outlineBound = true;
        }

        if (_renaming is not null)
        {
            return;
        }

        // a meta' animazione l'albero non si tocca: EndFold lo ricostruisce a movimento finito
        if (_folding)
        {
            return;
        }

        IReadOnlyList<OutlineRow> rows = OutlineTree.Build(
            _ws.Book.Sheets.Select(sheet => sheet.Outline()), _sheet?.Id, OutlineFilter.Text, _openBranches);

        HashSet<string> keys = [.. rows.Select(row => row.Key)];

        for (int i = _outline.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_outline[i].Key))
            {
                _outline.RemoveAt(i);
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (i < _outline.Count && _outline[i].Key == rows[i].Key)
            {
                _outline[i].Update(rows[i]);
            }
            else
            {
                _outline.Insert(i, new OutlineItem(rows[i]));
            }
        }

        while (_outline.Count > rows.Count)
        {
            _outline.RemoveAt(_outline.Count - 1);
        }

        int pages = _ws.Book.Sheets.Count;
        string many = pages == 1 ? "1 foglio" : $"{pages} fogli";
        OutlineFoot.Text = rows.Count == 0 ? $"{many}, niente che corrisponda" : many;
    }

    private void OnOutlineFilter(object sender, TextChangedEventArgs e) => RefreshOutline();

    /// <summary>La lente apre la ricerca sopra i fogli; richiusa, la ricerca si svuota.</summary>
    private void OnFilterToggle(object sender, RoutedEventArgs e) => ShowFilter(BtnFind.IsChecked == true);

    public void ShowFilter(bool on)
    {
        BtnFind.IsChecked = on;
        OutlineFilter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        if (on)
        {
            OutlineFilter.Focus();
            OutlineFilter.SelectAll();
        }
        else
        {
            OutlineFilter.Clear();
        }
    }

    private void OnFilterKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ShowFilter(false);
            Editor.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Una pagina porta in cima a se', sul suo titolo, anche se era gia' quella aperta; una
    /// sezione porta alla sua riga, scorrendo (si vede da dove si viene).
    /// </summary>
    private void OnOutlineClick(object sender, MouseButtonEventArgs e)
    {
        if (Inside(e.OriginalSource as DependencyObject, "toggle", "rename")
            || Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not OutlineItem item
            || _ws.Book.ById(item.Row.PageId) is not { } sheet)
        {
            return;
        }

        Wanted?.Invoke();

        if (!ReferenceEquals(sheet, _sheet))
        {
            Show(sheet);
        }

        Paragraph[] rows = Doc.Rows(sheet.Document);
        Paragraph? target = item.Row.IsPage ? rows.FirstOrDefault()
            : item.Row.HeadingIndex >= 0 && item.Row.HeadingIndex < rows.Length ? rows[item.Row.HeadingIndex] : null;

        if (target is null)
        {
            Editor.Focus();
            return;
        }

        if (item.Row.IsPage)
        {
            Editor.CaretPosition = target.ContentStart;
        }

        Glide(target);
    }

    private void OnOutlineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OutlineList.SelectedItem is OutlineItem { Row.IsPage: true })
        {
            OnRenamePage(sender, e);
        }
    }

    private void Glide(Paragraph target)
    {
        if (Doc.Scroller(Editor) is not { } view)
        {
            return;
        }

        Editor.UpdateLayout();
        Rect spot = target.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Motion.ScrollTo(view, Math.Max(0, view.VerticalOffset + spot.Top - HeadingAir));
        Editor.Focus();
    }

    private bool _folding;

    /// <summary>Se un'animazione non arrivasse in fondo, l'albero si sblocca comunque da solo.</summary>
    private DispatcherTimer? _foldGuard;

    private void StartFold()
    {
        _folding = true;
        if (_foldGuard is null)
        {
            _foldGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _foldGuard.Tick += (_, _) =>
            {
                if (_folding)
                {
                    EndFold();
                }
            };
        }

        _foldGuard.Stop();
        _foldGuard.Start();
    }

    /// <summary>
    /// Apre o chiude un ramo dell'albero con un'animazione: chiudendo, le righe figlie si
    /// accorciano e svaniscono dal basso verso l'alto; aprendo, crescono e compaiono in cascata
    /// dall'alto. Solo a movimento finito l'albero si ricostruisce, cosi' nulla salta.
    /// </summary>
    private void OnOutlineToggle(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OutlineItem item } || _folding)
        {
            return;
        }

        e.Handled = true;
        bool opening = !item.Row.IsOpen;

        if (!opening)
        {
            Fold(item);
            return;
        }

        _openBranches[item.Key] = true;
        RefreshOutline();

        // le righe nuove nascono adesso e partono gia' chiuse: nessun fotogramma a piena altezza
        OutlineList.UpdateLayout();
        Unfold(item);
    }

    /// <summary>Movimento finito: si ricostruisce l'albero (con quel che e' cambiato nel frattempo).</summary>
    private void EndFold()
    {
        _foldGuard?.Stop();
        _folding = false;
        RefreshOutline();
        OutlineList.UpdateLayout();
    }

    /// <summary>Le righe che stanno sotto a questa nell'albero, finche' non si risale al suo livello.</summary>
    private List<OutlineItem> Kids(OutlineItem item)
    {
        int at = _outline.IndexOf(item);
        List<OutlineItem> kids = [];

        for (int i = at + 1; at >= 0 && i < _outline.Count && _outline[i].Row.Depth > item.Row.Depth; i++)
        {
            kids.Add(_outline[i]);
        }

        return kids;
    }

    private const int FoldMs = 170;

    private void Fold(OutlineItem item)
    {
        List<ListBoxItem> rows = [.. Kids(item).Select(kid => OutlineList.ItemContainerGenerator.ContainerFromItem(kid)).OfType<ListBoxItem>()];

        if (rows.Count == 0)
        {
            _openBranches[item.Key] = false;
            RefreshOutline();
            return;
        }

        StartFold();
        double step = Math.Min(22, 140.0 / rows.Count);
        int place = 0;
        int left = rows.Count;

        // dal basso verso l'alto: il ramo si richiude sul suo titolo
        foreach (ListBoxItem row in Enumerable.Reverse(rows))
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(step * place++);
            var shrink = Motion.Slide(row.ActualHeight, 0, FoldMs, Motion.Ease(mode: System.Windows.Media.Animation.EasingMode.EaseIn));
            shrink.BeginTime = delay;
            var fade = Motion.Slide(1, 0, FoldMs * 4 / 5, Motion.Ease());
            fade.BeginTime = delay;
            shrink.Completed += (_, _) =>
            {
                if (--left > 0)
                {
                    return;
                }

                // le righe chiuse escono dalla lista a altezza zero: non tornano mai piene
                _openBranches[item.Key] = false;
                EndFold();
            };
            row.BeginAnimation(HeightProperty, shrink);
            row.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void Unfold(OutlineItem item)
    {
        int place = 0;
        List<OutlineItem> kids = Kids(item);
        double step = Math.Min(26, 190.0 / Math.Max(1, kids.Count));
        int left = kids.Count;

        if (left == 0)
        {
            return;
        }

        StartFold();
        foreach (OutlineItem kid in kids)
        {
            if (OutlineList.ItemContainerGenerator.ContainerFromItem(kid) is not ListBoxItem row)
            {
                if (--left == 0)
                {
                    EndFold();
                }

                continue;
            }

            TimeSpan delay = TimeSpan.FromMilliseconds(step * place++);
            double full = kid.RowHeight + row.Margin.Top + row.Margin.Bottom;
            row.Opacity = 0;
            // l'altezza resta a zero anche durante l'attesa: niente righe piene che poi si chiudono
            var grow = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
            grow.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
            grow.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(delay)));
            grow.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(full,
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(200)), Motion.Ease()));
            grow.Completed += (_, _) =>
            {
                row.BeginAnimation(HeightProperty, null);

                if (--left == 0)
                {
                    EndFold();
                }
            };
            var fade = Motion.Slide(0, 1, 240, Motion.Ease());
            fade.BeginTime = delay;
            fade.Completed += (_, _) =>
            {
                row.BeginAnimation(OpacityProperty, null);
                row.Opacity = 1;
            };
            row.BeginAnimation(HeightProperty, grow);
            row.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void OnAddPage(object sender, RoutedEventArgs e)
    {
        if (_ws.IsLocked || _ws.IsDamaged)
        {
            return;
        }

        Wanted?.Invoke();
        Sheet sheet = _ws.Book.Add();
        _ws.Touch();
        _ws.SaveNote();
        Show(sheet);
        _ws.Say($"Foglio nuovo: scrivi il titolo, oppure doppio clic sul nome nella sidebar. {_ws.Book.Sheets.Count} fogli.");

        if (_outline.FirstOrDefault(item => item.Row.IsPage && item.Row.PageId == sheet.Id) is { } fresh)
        {
            OutlineList.SelectedItem = fresh;
            Dispatcher.BeginInvoke(() => OnRenamePage(this, new RoutedEventArgs()), DispatcherPriority.Loaded);
        }
    }

    private void OnRenamePage(object sender, RoutedEventArgs e)
    {
        OutlineItem? item = OutlineList.SelectedItem as OutlineItem
            ?? _outline.FirstOrDefault(row => row.Row.IsPage && row.Row.PageId == _sheet?.Id);

        if (item is not { Row.IsPage: true })
        {
            _ws.Say("Scegli una pagina nella struttura, poi modificane il titolo.");
            return;
        }

        foreach (OutlineItem other in _outline)
        {
            other.Editing = false;
        }

        item.Editing = true;
        _renaming = item;

        Dispatcher.BeginInvoke(() =>
        {
            if (Field(item) is { } field)
            {
                field.Text = _ws.Book.ById(item.Row.PageId)?.Name ?? string.Empty;
                field.Focus();
                field.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keep(sender as TextBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            StopRenaming();
            e.Handled = true;
        }
    }

    /// <summary>Il nome si salva quando la casella perde il fuoco: cliccare altrove vuol dire "ho finito".</summary>
    private void OnRenameDone(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox field && !(e.NewFocus is DependencyObject next && IsWithin(field, next)))
        {
            Keep(field);
        }
    }

    private void Keep(TextBox? field)
    {
        if (_renaming is null || field is null || _ws.Book.ById(_renaming.Row.PageId) is not { } sheet)
        {
            StopRenaming();
            return;
        }

        string wanted = PageIds.Clean(field.Text);

        if (wanted != sheet.Name)
        {
            sheet.Name = wanted;
            _ws.Touch();
            _ws.SaveNote();
            _ws.Say(wanted.Length > 0 ? $"Titolo: {wanted}." : $"Titolo tolto: la pagina torna \"{PageIds.Untitled}\".");
        }

        StopRenaming();
    }

    private void StopRenaming()
    {
        if (_renaming is { } item)
        {
            item.Editing = false;
        }

        _renaming = null;
        RefreshOutline();
        ShowCounts();
    }

    private void OnDropPage(object sender, RoutedEventArgs e)
    {
        Sheet? sheet = OutlineList.SelectedItem is OutlineItem item ? _ws.Book.ById(item.Row.PageId) : _sheet;

        if (sheet is null)
        {
            return;
        }

        if (_ws.Book.Sheets.Count == 1)
        {
            _ws.Say("L'ultima pagina non si toglie.");
            return;
        }

        if (!DuckDialog.Confirm(Window.GetWindow(this), "Eliminare la pagina?",
                $"\"{sheet.Title}\" e tutto quel che contiene escono dalla nota. Resta solo nella copia di sicurezza fino al prossimo giro.",
                "Elimina pagina", danger: true))
        {
            return;
        }

        string title = sheet.Title;

        if (!_ws.Book.Remove(sheet))
        {
            return;
        }

        _ws.Touch();
        _ws.SaveNote();

        if (ReferenceEquals(sheet, _sheet))
        {
            Show(_ws.Book.Sheets[0]);
        }
        else
        {
            RefreshOutline();
        }

        _ws.SyncHosts(settled: true);
        _ws.Say($"Pagina \"{title}\" eliminata.");
    }

    private TextBox? Field(OutlineItem item) =>
        OutlineList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row ? Hunt<TextBox>(row) : null;

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

    private static T? Ancestor<T>(DependencyObject? from) where T : DependencyObject
    {
        while (from is not null and not T)
        {
            from = from is Visual ? VisualTreeHelper.GetParent(from) : LogicalTreeHelper.GetParent(from);
        }

        return from as T;
    }

    private static bool Inside(DependencyObject? from, params string[] names)
    {
        for (DependencyObject? step = from; step is not null; step = step is Visual ? VisualTreeHelper.GetParent(step) : LogicalTreeHelper.GetParent(step))
        {
            if (step is FrameworkElement { Name.Length: > 0 } named && names.Contains(named.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithin(DependencyObject owner, DependencyObject other)
    {
        for (DependencyObject? step = other; step is not null; step = step is Visual ? VisualTreeHelper.GetParent(step) : LogicalTreeHelper.GetParent(step))
        {
            if (ReferenceEquals(step, owner))
            {
                return true;
            }
        }

        return false;
    }
}
