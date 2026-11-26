using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Il foglio della nota visto dal Mac. I tasti vanno al "first responder" della finestra: dopo un
/// clic su un pulsante di Avalonia e' la vista di Avalonia, e il fuoco dato dentro alla pagina
/// (un campo, l'editor) non basta; qui il WKWebView diventa first responder, con la chiamata
/// documentata di AppKit. Le azioni di modifica (copy:, cut:, paste:) il WKWebView le fa da se':
/// gli si mandano direttamente, come farebbe il menu Modifica di un'app Cocoa. Altrove non serve
/// nulla: Give lascia fare al controllo, Edit risponde false e si usa la strada della pagina.
/// </summary>
internal static class NativeFocus
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Act(IntPtr receiver, IntPtr selector, IntPtr sender);

    public static void Give(NativeWebView web)
    {
        if (View(web) is not { } view)
        {
            return;
        }

        try
        {
            IntPtr window = Send(view, Selector("window"));

            if (window != IntPtr.Zero)
            {
                Send(window, Selector("makeFirstResponder:"), view);
            }
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // senza AppKit raggiungibile resta il fuoco di Avalonia: un clic nel foglio basta
        }
    }

    /// <summary>
    /// Un'azione standard di modifica ("copy:", "cut:", "paste:", "selectAll:") mandata al
    /// WKWebView. Falso fuori dal Mac, o se il WKWebView non la conosce.
    /// </summary>
    public static bool Edit(NativeWebView web, string action)
    {
        if (View(web) is not { } view)
        {
            return false;
        }

        try
        {
            IntPtr selector = Selector(action);

            if (!Send(view, Selector("respondsToSelector:"), selector))
            {
                return false;
            }

            Act(view, selector, IntPtr.Zero);
            return true;
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    private static IntPtr? View(NativeWebView web)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        return web.TryGetPlatformHandle() is IAppleWKWebViewPlatformHandle { WKWebView: var view } && view != IntPtr.Zero ? view : null;
    }
}
