namespace DuckNote.App.Scanning;

/// <summary>Quel che serve solo alla tabella WPF.</summary>
public sealed partial class HostRow
{
    public System.Windows.Visibility NoteVis => FromNote ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
}
