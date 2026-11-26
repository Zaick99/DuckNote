using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DuckNote.App.Ui;

/// <summary>Un oggetto che avvisa quando cambia: le righe delle tabelle (WPF e Avalonia).</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
