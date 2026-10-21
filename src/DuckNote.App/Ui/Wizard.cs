using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DuckNote.App.Theme;

namespace DuckNote.App.Ui;

/// <summary>Un passo del wizard: etichetta nello stepper + pannello (gia' presente nel XAML, Collapsed).</summary>
public sealed record WizardStep(string Label, FrameworkElement Panel);

/// <summary>
/// Procedura guidata: stepper a pallini (fatto / corrente / da fare, tratto che si riempie) e
/// pannelli che scorrono con <see cref="Fx.Carousel"/> (avanti da destra, indietro da sinistra).
/// I pannelli stanno tutti nella stessa Grid con ClipToBounds="True"; il wizard li mostra uno alla volta.
/// <code>
/// _wiz = new Wizard(Stepper, [new("Origine", PanelA), new("Opzioni", PanelB), new("Riepilogo", PanelC)]);
/// _wiz.Changed += i => { BtnBack.Visibility = i == 0 ? Visibility.Hidden : Visibility.Visible; ... };
/// _wiz.Go(0, animate: false);
/// </code>
/// </summary>
public sealed class Wizard
{
    private readonly Panel _stepper;
    private IReadOnlyList<WizardStep> _steps;
    private bool _done;
    private readonly HashSet<FrameworkElement> _known = [];

    public Wizard(Panel stepper, IReadOnlyList<WizardStep> steps)
    {
        _stepper = stepper;
        _steps = steps;
    }

    public int Index { get; private set; }
    public IReadOnlyList<WizardStep> Steps => _steps;
    public bool IsLast => Index == _steps.Count - 1;

    /// <summary>Prima di cambiare passo (indice di destinazione): utile per preparare il riepilogo.</summary>
    public event Action<int>? Entering;
    /// <summary>Dopo il cambio: aggiorna i pulsanti Avanti/Indietro.</summary>
    public event Action<int>? Changed;
    /// <summary>Validazione: restituisce false per bloccare l'avanzamento oltre il passo indicato.</summary>
    public Func<int, bool>? CanLeave { get; set; }

    /// <summary>Cambia l'elenco dei passi (es. ramo diverso scelto al primo passo) restando sul passo corrente.</summary>
    public void SetSteps(IReadOnlyList<WizardStep> steps)
    {
        _steps = steps;
        Index = Math.Clamp(Index, 0, steps.Count - 1);
        Build();
    }

    public void Next() { if (CanLeave?.Invoke(Index) != false) Go(Index + 1); }
    public void Back() => Go(Index - 1);

    public void Go(int index, bool animate = true)
    {
        index = Math.Clamp(index, 0, _steps.Count - 1);
        var forward = index >= Index;
        Index = index;
        _done = false;
        Entering?.Invoke(index);
        Show(_steps[index].Panel, forward, animate);
        Build();
        Changed?.Invoke(index);
    }

    /// <summary>Mostra un pannello finale fuori dai passi (es. "Fatto" con coriandoli): lo stepper diventa tutto spuntato.</summary>
    public void Finish(FrameworkElement donePanel)
    {
        _done = true;
        Show(donePanel, true, true);
        Build();
    }

    private void Show(FrameworkElement panel, bool forward, bool animate)
    {
        // tutti i pannelli mai mostrati (anche quelli di Finish): uno solo resta visibile
        _known.UnionWith(_steps.Select(s => s.Panel));
        _known.Add(panel);
        var all = _known.ToList();
        var old = all.FirstOrDefault(p => p != panel && p.Visibility == Visibility.Visible);
        foreach (var p in all) if (p != panel && p != old) p.Visibility = Visibility.Collapsed;
        if (!animate || old is null)
        {
            if (old is not null) old.Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Visible;
            return;
        }
        Fx.Carousel(old, panel, forward);
    }

    private void Build()
    {
        _stepper.Children.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            var state = _done || i < Index ? "done" : i == Index ? "current" : "todo";
            var circle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5) };
            var num = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold };
            switch (state)
            {
                case "done":
                    circle.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                    circle.SetResourceReference(Border.BorderBrushProperty, "Accent");
                    num.Text = "\uE73E";
                    num.FontFamily = (FontFamily)_stepper.FindResource("FIcons");
                    num.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                    break;
                case "current":
                    circle.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                    circle.SetResourceReference(Border.BorderBrushProperty, "Accent");
                    num.Text = (i + 1).ToString();
                    num.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                    break;
                default:
                    circle.SetResourceReference(Border.BorderBrushProperty, "BorderControl");
                    num.Text = (i + 1).ToString();
                    num.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
                    break;
            }
            circle.Child = num;
            var label = new TextBlock { Text = _steps[i].Label, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = state == "current" ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, state == "todo" ? "LabelTertiary" : "Label");
            var btn = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { circle, label } },
                Style = (Style)_stepper.FindResource("Toolbtn"), Padding = new Thickness(6, 0, 8, 0), Height = 32, Tag = i, Cursor = Cursors.Hand,
            };
            // si torna liberamente indietro; in avanti solo un passo alla volta (passa dalla validazione)
            btn.Click += (s, _) =>
            {
                var target = (int)((Button)s!).Tag;
                if (target < Index) Go(target);
                else if (target == Index + 1) Next();
            };
            _stepper.Children.Add(btn);

            if (state == "current") Fx.Pop(circle);
            if (i < _steps.Count - 1)
            {
                var line = new Border { Width = 34, Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
                line.SetResourceReference(Border.BackgroundProperty, "Separator");
                _stepper.Children.Add(line);
                if (_done || i < Index)
                {
                    var fill = new Border { Height = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Width = 34 };
                    fill.SetResourceReference(Border.BackgroundProperty, "Accent");
                    line.Child = fill;
                    // il tratto appena completato si riempie da sinistra a destra
                    if (i == Index - 1 && !_done) fill.BeginAnimation(FrameworkElement.WidthProperty, Motion.Slide(0, 34, 420, Motion.Ease("Quint")));
                }
            }
        }
    }
}
