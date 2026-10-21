using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuckNote.App.Theme;
using DuckNote.Notes.Charts;

namespace DuckNote.App.Views;

/// <summary>Il testo del grafico a sinistra, il grafico che si ridisegna a destra mentre si scrive.</summary>
public partial class ChartDialog : Window
{
    private ChartDialog(string written)
    {
        InitializeComponent();
        Written.Text = written;

        Loaded += (_, _) =>
        {
            Motion.OpenDialog(Root);
            Written.Focus();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
    }

    public static string? Ask(Window? owner, string written)
    {
        ChartDialog dialog = new(written);

        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.Written.Text : null;
    }

    private void OnWritten(object sender, TextChangedEventArgs e)
    {
        if (Preview is not null)
        {
            Preview.Spec = ChartSpec.Read(Written.Text);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Motion.CloseDialog(this, Root, true);

    private void OnCancel(object sender, RoutedEventArgs e) => Motion.CloseDialog(this, Root, false);
}
