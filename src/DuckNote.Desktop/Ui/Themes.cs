using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// I ControlTheme di Duck.axaml stanno fra le risorse dell'applicazione. Un controllo creato
/// in codice non e' ancora nell'albero quando gli si assegna il tema, e cercarlo da li' non
/// arriva all'applicazione: si cerca sempre dall'alto.
/// </summary>
public static class Themes
{
    public static ControlTheme? Of(string key) =>
        Application.Current?.TryFindResource(key, out object? theme) == true ? theme as ControlTheme : null;
}
