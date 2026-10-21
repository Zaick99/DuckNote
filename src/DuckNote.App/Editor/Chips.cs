using System.Windows;
using System.Windows.Controls;

namespace DuckNote.App.Editor;

/// <summary>I tastini dei riquadri: un glifo del tema, lo stile Toolbtn del kit.</summary>
internal static class Chips
{
    public static Button Icon(string glyph, string tip, Action press, string? tone = null)
    {
        TextBlock icon = new() { Text = glyph, FontSize = 12 };
        icon.Style = (Style)Application.Current.FindResource("Icon");

        if (tone is not null)
        {
            icon.SetResourceReference(TextBlock.ForegroundProperty, tone);
        }

        Button tap = new()
        {
            Content = icon,
            Style = (Style)Application.Current.FindResource("Toolbtn"),
            Width = 24,
            Height = 24,
            MinWidth = 0,
            Padding = new Thickness(0),
            ToolTip = tip,
            Focusable = false
        };

        tap.Click += (_, _) => press();

        return tap;
    }
}
