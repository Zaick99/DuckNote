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

/// <summary>
/// Pagina che accompagna la sidebar quando entra o esce. La finestra le ha gia' dato la
/// larghezza nuova e la sta facendo scivolare da <c>from</c> a zero, in <c>ms</c> millisecondi
/// con quella curva: qui la pagina tiene fermo quello che non deve muoversi (cio' che sta a
/// destra o al centro), con la stessa durata e la stessa curva.
/// </summary>
public interface ISlidingPage
{
    void Slide(double from, int ms, System.Windows.Media.Animation.IEasingFunction ease);
}
