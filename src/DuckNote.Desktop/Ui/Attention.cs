using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// Chiamare l'attenzione quando DuckNote e' dietro alle altre finestre: sul Mac l'icona nel Dock
/// salta (NSApplication requestUserAttention:, la chiamata documentata di AppKit). Nessun permesso
/// da chiedere, nessuna notifica di sistema. Fuori dal Mac non fa nulla.
/// </summary>
internal static class Attention
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSCriticalRequest: salta finche' l'app non torna davanti.</summary>
    private const long Critical = 0;

    /// <summary>NSInformationalRequest: un salto solo.</summary>
    private const long Informational = 10;

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr Class(string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern long Send(IntPtr receiver, IntPtr selector, long argument);

    /// <summary>Se la finestra non e' davanti, l'icona nel Dock salta (finche' non si torna, se <paramref name="insist"/>).</summary>
    public static void Request(Window? window, bool insist)
    {
        if (!OperatingSystem.IsMacOS() || window is { IsActive: true })
        {
            return;
        }

        try
        {
            IntPtr app = Send(Class("NSApplication"), Selector("sharedApplication"));

            if (app != IntPtr.Zero)
            {
                Send(app, Selector("requestUserAttention:"), insist ? Critical : Informational);
            }
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // senza AppKit raggiungibile resta l'avviso nella finestra
        }
    }
}
