using System.Windows;
using System.Windows.Media;

namespace DuckNote.App.Theme;

public static class Finishes
{
    public static void Install(ResourceDictionary resources, string theme)
    {
        bool dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);

        SolidColorBrush rim = new(ThemeTokens.Parse(dark ? "#33FFFFFF" : "#1F000000"));
        rim.Freeze();
        resources["GlassRim"] = rim;

        SolidColorBrush field = new(ThemeTokens.Parse(dark ? "#40000000" : "#59FFFFFF"));
        field.Freeze();
        resources["GlassField"] = field;

        LinearGradientBrush accent = new()
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        accent.GradientStops.Add(new GradientStop(ThemeTokens.Parse("#FFD700"), 0));
        accent.GradientStops.Add(new GradientStop(ThemeTokens.Parse("#FF8C00"), 1));
        accent.Freeze();
        resources["AccentFill"] = accent;
    }
}
