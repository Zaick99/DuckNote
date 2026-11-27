using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DuckNote.App.Ui;

/// <summary>
/// Il WebView2 "di composizione" (quello che si vede anche nella finestra trasparente di DuckNote)
/// che non fa cadere la finestra: se il motore non si costruisce (un pezzo di Windows che manca,
/// la cattura grafica non disponibile) lo dice con <see cref="Failed"/> invece di lanciare
/// un'eccezione in mezzo all'impaginazione. Gli errori che il controllo lancia dai suoi eventi
/// arrivano all'App, che li riconosce con <see cref="FromWebView"/> e li passa a <see cref="Report"/>.
/// </summary>
public sealed class SafeWebView : WebView2CompositionControl
{
    /// <summary>Un errore del motore arrivato da fuori (dall'App): lo sentono tutte le pagine web.</summary>
    public static event Action<Exception>? EngineFailed;

    /// <summary>Questo controllo non e' partito.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Perche' non e' partito; null finche' va tutto bene.</summary>
    public Exception? Problem { get; private set; }

    public SafeWebView()
    {
        EngineFailed += Fail;
    }

    public override void OnApplyTemplate()
    {
        try
        {
            base.OnApplyTemplate();
        }
        catch (Exception problem) when (IsEngineProblem(problem))
        {
            Fail(problem);
        }
    }

    public void Fail(Exception problem)
    {
        if (Problem is not null)
        {
            return;
        }

        Problem = problem;
        // fuori dall'impaginazione in corso: il controllo si spegne, la pagina dice cosa e' successo
        Dispatcher.BeginInvoke(() =>
        {
            Visibility = Visibility.Collapsed;
            Failed?.Invoke(problem);
        });
    }

    /// <summary>Un errore del motore che l'App ha preso al volo: le pagine web si spengono e lo dicono.</summary>
    public static void Report(Exception problem) => EngineFailed?.Invoke(problem);

    public static bool IsEngineProblem(Exception problem) =>
        problem is FileNotFoundException or FileLoadException or TypeLoadException or MissingMethodException or COMException
            or PlatformNotSupportedException or NotSupportedException or ArgumentException or InvalidOperationException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or WebView2RuntimeNotFoundException;

    /// <summary>L'eccezione viene da dentro il controllo WebView2 (il suo Loaded, la cattura grafica, le librerie di Windows)?</summary>
    public static bool FromWebView(Exception problem)
    {
        for (Exception? at = problem; at is not null; at = at.InnerException)
        {
            if ((at.StackTrace ?? string.Empty).Contains("Microsoft.Web.WebView2", StringComparison.Ordinal)
                || at is FileNotFoundException { FileName: { } file } && file.Contains("Microsoft.Windows.SDK.NET", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
