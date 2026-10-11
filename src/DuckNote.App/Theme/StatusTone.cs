using DuckNote.App.Models;

namespace DuckNote.App.Theme;

public readonly record struct StatusTone(string Ink, string Wash);

public static class StatusTones
{
    public static StatusTone Of(int rank) => rank switch
    {
        <= 1 => new StatusTone("Green", "GreenSoft"),
        < ScanRow.NeverChecked => new StatusTone("Red", "RedSoft"),
        _ => new StatusTone("Orange", "OrangeSoft")
    };

    public static StatusTone Blank => new("LabelQuaternary", "SeparatorSoft");
}
