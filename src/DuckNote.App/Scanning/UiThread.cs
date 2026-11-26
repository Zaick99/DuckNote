namespace DuckNote.App.Scanning;

/// <summary>
/// Il thread dell'interfaccia, chiunque sia a disegnarla (WPF o Avalonia): l'app lo dichiara
/// all'avvio con <see cref="Use"/>. Senza dichiarazione tutto gira dove si trova.
/// </summary>
public static class UiThread
{
    private static Func<bool>? _here;
    private static Func<Action, Task>? _run;

    public static void Use(Func<bool> here, Func<Action, Task> run)
    {
        _here = here;
        _run = run;
    }

    /// <summary>Si sta lavorando fuori dal thread dell'interfaccia.</summary>
    public static bool Elsewhere() => _here is { } here && !here();

    public static Task Run(Action work)
    {
        if (_run is { } run)
        {
            return run(work);
        }

        work();
        return Task.CompletedTask;
    }
}
