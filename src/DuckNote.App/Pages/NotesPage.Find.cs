using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using DuckNote.App.Editor;
using DuckNote.App.Ui;

namespace DuckNote.App.Pages;

public partial class NotesPage
{
    /// <summary>Ctrl+F apre la ricerca nella pagina, Ctrl+Maiusc+F quella nei fogli, F3 e Maiusc+F3 camminano, Esc chiude: lo chiama la finestra.</summary>
    public bool HandleShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenFind();
            return true;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowFilter(true);
            return true;
        }

        if (FindBar.Visibility != Visibility.Visible)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            CloseFind();
            return true;
        }

        if (e.Key == Key.F3)
        {
            Hop(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
            return true;
        }

        return false;
    }

    private void OnFindToggle(object sender, RoutedEventArgs e)
    {
        if (FindBar.Visibility == Visibility.Visible)
        {
            CloseFind();
        }
        else
        {
            OpenFind();
        }
    }

    private void OpenFind()
    {
        string picked = Editor.Selection.Text.Trim('\r', '\n', ' ', '\t');

        if (picked.Length is > 0 and <= 120 && !picked.Contains('\n'))
        {
            FindBox.Text = picked;
        }

        Fx.SlideVisible(FindBar, true, -14);
        FindBox.Focus();
        FindBox.SelectAll();
        Search(jump: false);
    }

    private void CloseFind()
    {
        Fx.SlideVisible(FindBar, false, -14);
        Editor.Focus();
    }

    private void OnFindClose(object sender, RoutedEventArgs e) => CloseFind();

    private void OnFindText(object sender, TextChangedEventArgs e) => Search(jump: true);

    private void OnFindCase(object sender, RoutedEventArgs e) => Search(jump: false);

    private void OnFindNext(object sender, RoutedEventArgs e) => Hop(1);

    private void OnFindPrev(object sender, RoutedEventArgs e) => Hop(-1);

    private void OnFindKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Hop(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
        }
    }

    private void OnFindSwapToggle(object sender, RoutedEventArgs e)
    {
        bool shown = FindSwap.IsChecked == true;
        SwapRow.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        (shown ? (Control)SwapBox : FindBox).Focus();
    }

    private void Search(bool jump)
    {
        _finder.Look(FindBox.Text, FindCase.IsChecked == true);
        Tally();

        if (jump && FindBox.Text.Length > 0)
        {
            Hop(1);
        }
    }

    private void Hop(int direction)
    {
        bool exact = FindCase.IsChecked == true;

        if (_finder.Needle != FindBox.Text)
        {
            _finder.Look(FindBox.Text, exact);
        }

        if (_finder.Step(direction, Editor.Selection.Start) is { } hit)
        {
            Reveal(hit);
        }
        else if (FindBox.Text.Length > 0)
        {
            Fx.Shake(FindBox);
        }

        Tally();
    }

    private void Reveal(TextRange hit)
    {
        Editor.Selection.Select(hit.Start, hit.End);

        if (Doc.Scroller(Editor) is not { } view)
        {
            return;
        }

        Editor.UpdateLayout();
        Rect spot = hit.Start.GetCharacterRect(LogicalDirection.Forward);
        double wanted = view.VerticalOffset + spot.Top - view.ViewportHeight / 3;
        Theme.Motion.ScrollTo(view, Math.Clamp(wanted, 0, Math.Max(0, view.ScrollableHeight)));
    }

    private void Tally() =>
        FindCount.Text = FindBox.Text.Length == 0 ? string.Empty
            : _finder.Count == 0 ? "0"
            : string.Create(CultureInfo.CurrentCulture, $"{_finder.Current}/{_finder.Count}");

    private void OnSwapOne(object sender, RoutedEventArgs e)
    {
        if (_finder.Here() is null)
        {
            Hop(1);
        }

        if (!_finder.Swap(SwapBox.Text))
        {
            _ws.Say("Nessuna occorrenza da sostituire.");
            return;
        }

        Tally();
        Hop(1);
    }

    private void OnSwapAll(object sender, RoutedEventArgs e)
    {
        if (FindBox.Text.Length == 0)
        {
            return;
        }

        _finder.Look(FindBox.Text, FindCase.IsChecked == true);
        int done = _finder.SwapAll(SwapBox.Text);
        Tally();

        _ws.Say(done switch
        {
            0 => "Nessuna occorrenza da sostituire.",
            1 => "Una occorrenza sostituita.",
            _ => $"{done} occorrenze sostituite."
        });
    }
}
