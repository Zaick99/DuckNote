namespace DuckNote.Notes.Settings;

/// <summary>
/// Dove vivono i dati: %APPDATA%\duck\DuckNote su Windows, ~/Library/Application Support/duck/DuckNote
/// su macOS, ~/.config/duck/DuckNote su Linux, accanto alle altre app duck, oppure la
/// cartella in DUCKNOTE_HOME (per tenere nota e cassaforte su una chiavetta accanto
/// all'eseguibile). I dati delle versioni che scrivevano in %APPDATA%\DuckNote vengono
/// spostati al primo avvio.
/// </summary>
public static class AppPaths
{
    public static string Folder { get; } = Home();

    public static string Note => Path.Combine(Folder, "note.xaml");

    public static string NoteBackup => Note + ".bak";

    public static string Settings => Path.Combine(Folder, "settings.json");

    public static string LastScan => Path.Combine(Folder, "lastscan.json");

    public static string Store => Path.Combine(Folder, "store.bin");

    public static string Oui => Path.Combine(Folder, "oui.txt");

    public static string Lock => Path.Combine(Folder, "aperto.lock");

    public static void Ensure() => Directory.CreateDirectory(Folder);

    private static string Home() =>
        Resolve(SystemData(), Environment.GetEnvironmentVariable("DUCKNOTE_HOME"));

    /// <summary>
    /// La cartella dei dati delle applicazioni del sistema. Su macOS e' Application Support nella
    /// Libreria dell'utente (.NET li' risponderebbe ~/.config, che e' l'uso di Linux).
    /// </summary>
    private static string SystemData() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// La cartella dei dati. Con DUCKNOTE_HOME quella; altrimenti appData\duck\DuckNote. Se
    /// esiste solo la cartella delle versioni precedenti (appData\DuckNote) la si sposta li';
    /// se lo spostamento non riesce (un file aperto, permessi) si resta su quella vecchia,
    /// perche' una nota che sparisce e' peggio di una cartella con il nome di prima.
    /// </summary>
    public static string Resolve(string appData, string? home)
    {
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        string wanted = Path.Combine(appData, "duck", "DuckNote");
        string old = Path.Combine(appData, "DuckNote");

        if (Directory.Exists(wanted) || !Directory.Exists(old))
        {
            return wanted;
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(appData, "duck"));
            Directory.Move(old, wanted);
            return wanted;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return old;
        }
    }
}
