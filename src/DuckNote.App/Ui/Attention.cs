using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DuckNote.App.Ui;

/// <summary>
/// Chiamare l'attenzione quando DuckNote e' dietro alle altre finestre: il pulsante nella barra
/// delle applicazioni lampeggia (FlashWindowEx). Nessun permesso da chiedere, nessuna notifica di
/// sistema; smette da solo quando si torna sulla finestra.
/// </summary>
internal static class Attention
{
    private const uint FlashTray = 0x2;
    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Se la finestra non e' davanti, la barra delle applicazioni lampeggia (finche' non si torna, se <paramref name="insist"/>).</summary>
    public static void Request(Window? window, bool insist)
    {
        if (window is null || window.IsActive)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        FlashInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = insist ? FlashTray | FlashUntilForeground : FlashTray,
            Count = insist ? uint.MaxValue : 3,
            Timeout = 0
        };

        try
        {
            FlashWindowEx(ref info);
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException)
        {
            // senza user32 resta l'avviso nella finestra
        }
    }
}
