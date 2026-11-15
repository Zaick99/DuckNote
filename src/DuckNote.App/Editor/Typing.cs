using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DuckNote.App.Editor;

/// <summary>
/// Un RichTextBox non inserisce subito i caratteri digitati: li mette in coda e li scrive a
/// priorita' bassa, per raggrupparli. Chi tocca il documento mentre la coda e' piena (Invio,
/// Spazio che formatta, una scorciatoia, la pittura degli indirizzi) arriva PRIMA delle ultime
/// lettere battute: scrivendo in fretta l'a capo finisce a meta' parola. Prima di toccare il
/// documento si svuota quindi la coda, come fanno i comandi dell'editor stesso.
/// </summary>
public static class Typing
{
    private static readonly PropertyInfo? Editor =
        typeof(TextBoxBase).GetProperty("TextEditor", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? FlushPending =
        typeof(RichTextBox).Assembly.GetType("System.Windows.Documents.TextEditorTyping")
            ?.GetMethod("_FlushPendingInputItems", BindingFlags.Static | BindingFlags.NonPublic);

    /// <summary>La coda si sa svuotare? (false solo se una versione futura di WPF cambiasse i nomi)</summary>
    public static bool CanFlush => Editor is not null && FlushPending is not null;

    /// <summary>Scrive subito nel documento i caratteri digitati e non ancora inseriti.</summary>
    public static void Flush(RichTextBox editor)
    {
        if (Editor is null || FlushPending is null)
        {
            return;
        }

        try
        {
            if (Editor.GetValue(editor) is { } text)
            {
                FlushPending.Invoke(null, [text]);
            }
        }
        catch (Exception problem) when (problem is TargetInvocationException or ArgumentException or MethodAccessException)
        {
            // senza svuotamento si torna al comportamento di prima: mai un errore per questo
        }
    }
}
