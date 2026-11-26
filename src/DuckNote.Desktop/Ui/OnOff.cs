using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// L'interruttore delle impostazioni, come lo "Switch" della versione WPF: binario 38x22, pomello
/// bianco con l'ombra, che scorre di 16 punti in 220 ms con un piccolo rimbalzo (BackEase 0.3).
/// Il movimento lo fa questo codice: il ToggleSwitch di Fluent avrebbe la sua animazione.
/// </summary>
public sealed class OnOff : ToggleButton
{
    private const double Travel = 16;
    private TranslateTransform? _shift;

    public OnOff()
    {
        Theme = Themes.Of("Switch");
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Knob") is { } knob)
        {
            _shift = new TranslateTransform(IsChecked == true ? Travel : 0, 0);
            knob.RenderTransform = _shift;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty && _shift is { } shift)
        {
            double to = IsChecked == true ? Travel : 0;

            if (!IsEffectivelyVisible || VisualRoot is null)
            {
                shift.X = to;
                return;
            }

            Motion.Tween((this, "knob"), shift.X, to, 220, x => shift.X = x, null, Curves.BackOut(0.3));
        }
    }
}
