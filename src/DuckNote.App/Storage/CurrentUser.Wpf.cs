namespace DuckNote.App.Storage;

public static partial class CurrentUser
{
    /// <summary>L'immagine della papera dell'avatar, fra le risorse dell'app WPF.</summary>
    public static Uri DuckFor(string name) => new($"pack://application:,,,/Assets/avatars/duck{DuckIndex(name):00}.png");
}
