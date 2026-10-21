namespace DuckNote.Notes.Settings;

/// <summary>
/// Dove vivono i dati: %APPDATA%\DuckNote, oppure la cartella in DUCKNOTE_HOME (per tenere
/// nota e cassaforte su una chiavetta accanto all'eseguibile).
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

    private static string Home()
    {
        string? elsewhere = Environment.GetEnvironmentVariable("DUCKNOTE_HOME");

        return string.IsNullOrWhiteSpace(elsewhere)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuckNote")
            : elsewhere;
    }
}
