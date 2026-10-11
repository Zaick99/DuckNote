using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DuckNote.App.Editor;
using DuckNote.App.Theme;

namespace DuckNote.App;

public partial class MainWindow
{
    private Finder _finder = null!;

    private void SetUpFind()
    {
        _finder = new Finder(Editor);

        Editor.IsInactiveSelectionHighlightEnabled = true;

        FindBox.TextChanged += (_, _) => Search(jump: true);
        FindBox.KeyDown += OnFindKey;

        FindNext.Click += (_, _) => Hop(1);
        FindPrev.Click += (_, _) => Hop(-1);
        FindClose.Click += (_, _) => CloseFind();

        FindCase.Checked += (_, _) => Search(jump: false);
        FindCase.Unchecked += (_, _) => Search(jump: false);

        FindSwap.Checked += (_, _) => ShowReplace(true);
        FindSwap.Unchecked += (_, _) => ShowReplace(false);

        ReplOne.Click += (_, _) => SwapHere();
        ReplAll.Click += (_, _) => SwapEverywhere();

        PreviewKeyDown += OnFindShortcut;
    }

    private void OnFindShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenFind();
            e.Handled = true;
            return;
        }

        if (FindBar.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            Hop(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
        }
    }

    private void OnFindKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        Hop(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
        e.Handled = true;
    }

    private void ToggleFind()
    {
        if (FindBar.Visibility == Visibility.Visible)
        {
            CloseFind();
            return;
        }

        OpenFind();
    }

    private void OpenFind()
    {
        TabNote.IsChecked = true;

        if (Editor.Selection is { IsEmpty: false } picked && OneLine(picked.Text) is { } word)
        {
            FindBox.Text = word;
        }

        if (FindBar.Visibility != Visibility.Visible)
        {
            FindBar.Visibility = Visibility.Visible;

            IEasingFunction ease = Motion.Ease();
            FindBar.BeginAnimation(OpacityProperty, Motion.Slide(0, 1, 170, ease));
            FindBarT.BeginAnimation(TranslateTransform.YProperty, Motion.Slide(-14, 0, 240, ease));
            FindBarS.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Slide(0.9, 1, 240, ease));
        }

        FindBox.Focus();
        FindBox.SelectAll();
        Search(jump: false);
    }

    private void CloseFind()
    {
        DoubleAnimation fade = Motion.Slide(FindBar.Opacity, 0, 140, Motion.Ease(mode: EasingMode.EaseIn));
        fade.Completed += (_, _) => FindBar.Visibility = Visibility.Collapsed;

        FindBar.BeginAnimation(OpacityProperty, fade);
        FindBarT.BeginAnimation(TranslateTransform.YProperty,
            Motion.Slide(0, -14, 140, Motion.Ease(mode: EasingMode.EaseIn)));

        Editor.Focus();
    }

    private void ShowReplace(bool shown)
    {
        ReplRow.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        if (shown)
        {
            ReplBox.Focus();
            return;
        }

        FindBox.Focus();
    }

    private void Search(bool jump)
    {
        _lastExact = FindCase.IsChecked == true;
        _finder.Look(FindBox.Text, _lastExact);
        Tally();

        if (jump && FindBox.Text.Length > 0)
        {
            Hop(1);
        }
    }

    private void Hop(int direction)
    {
        bool exact = FindCase.IsChecked == true;

        if (_finder.Needle != FindBox.Text || exact != _lastExact)
        {
            _lastExact = exact;
            _finder.Look(FindBox.Text, exact);
        }

        if (_finder.Step(direction, Editor.Selection.Start) is { } hit)
        {
            Reveal(hit);
        }

        Tally();
    }

    private bool _lastExact;

    private void Reveal(TextRange hit)
    {
        Editor.Selection.Select(hit.Start, hit.End);
        Editor.UpdateLayout();

        if (Editor.Template?.FindName("PART_ContentHost", Editor) is not ScrollViewer view)
        {
            return;
        }

        Rect spot = hit.Start.GetCharacterRect(LogicalDirection.Forward);
        double wanted = view.VerticalOffset + spot.Top - (view.ViewportHeight / 3);

        Motion.ScrollTo(view, Math.Clamp(wanted, 0, Math.Max(0, view.ScrollableHeight)));
    }

    private void Tally()
    {
        if (FindBox.Text.Length == 0)
        {
            FindCount.Text = string.Empty;
            return;
        }

        FindCount.Text = _finder.Count == 0 ? "0" : $"{_finder.Current}/{_finder.Count}";
    }

    private void SwapHere()
    {
        if (_finder.Here() is null)
        {
            Hop(1);
        }

        if (!_finder.Swap(ReplBox.Text))
        {
            StatusText.Text = "Nessuna occorrenza da sostituire.";
            return;
        }

        Tally();
        Hop(1);
    }

    private void SwapEverywhere()
    {
        if (FindBox.Text.Length == 0)
        {
            return;
        }

        _finder.Look(FindBox.Text, FindCase.IsChecked == true);
        int done = _finder.SwapAll(ReplBox.Text);

        Tally();
        StatusText.Text = done switch
        {
            0 => "Nessuna occorrenza da sostituire.",
            1 => "Una occorrenza sostituita.",
            _ => $"{done} occorrenze sostituite."
        };
    }

    private static string? OneLine(string text)
    {
        string trimmed = text.Trim('\r', '\n', ' ', '\t');

        return trimmed.Length is > 0 and <= 120 && !trimmed.Contains('\n') ? trimmed : null;
    }
}
