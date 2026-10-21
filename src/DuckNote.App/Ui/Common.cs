using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace DuckNote.App.Ui;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public static class Busy
{
    /// <summary>Disabilita il bottone e ne cambia il testo finche' il lavoro non e' finito (niente doppi clic).</summary>
    public static async Task Run(Button button, string busyText, Func<Task> work)
    {
        var old = button.Content;
        button.IsEnabled = false;
        button.Content = busyText;
        try { await work(); }
        finally { button.Content = old; button.IsEnabled = true; }
    }
}

/// <summary>Pagina ospitata dalla ShellWindow: OnShown viene chiamato a ogni apertura.</summary>
public interface IDuckPage
{
    void OnShown();
}
