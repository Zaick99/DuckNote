using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace DuckNote.Desktop.Ui;

/// <summary>
/// I semafori del Mac (chiudi, riduci, ingrandisci) dentro alla fascia del titolo di DuckNote,
/// alta 46 punti: centrati in altezza e allineati ai nostri pulsanti, non in alto come in una
/// barra del titolo normale di 28. Si fa come trafficLightPosition di Electron: la fascia di
/// sistema che li contiene si alza alla misura della nostra e ogni pulsante si sposta al suo
/// posto. AppKit la ridispone quando la finestra cambia (misura, tutto schermo), quindi si
/// riapplica ogni volta; a tutto schermo i semafori li gestisce il sistema e non si toccano.
/// </summary>
internal static class TrafficLights
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, long argument);

    // NSRect (32 byte): su arm64 torna nei registri, su x86_64 passa da objc_msgSend_stret
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern Rect SendRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")]
    private static extern void SendRectStret(out Rect result, IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SetFrame(IntPtr receiver, IntPtr selector, Rect frame);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SetOrigin(IntPtr receiver, IntPtr selector, Point origin);

    /// <summary>
    /// Mette i semafori in una fascia alta <paramref name="height"/> punti, il primo a
    /// <paramref name="left"/> punti dal bordo sinistro. Fuori dal Mac, o a tutto schermo, nulla.
    /// </summary>
    public static void Place(Window window, double height, double left)
    {
        if (!OperatingSystem.IsMacOS() || window.WindowState == WindowState.FullScreen)
        {
            return;
        }

        try
        {
            if (window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle { NSWindow: var ns } || ns == IntPtr.Zero)
            {
                return;
            }

            IntPtr pick = Selector("standardWindowButton:");
            IntPtr[] buttons = [Send(ns, pick, 0), Send(ns, pick, 1), Send(ns, pick, 2)];

            if (buttons.Any(button => button == IntPtr.Zero))
            {
                return;
            }

            IntPtr superview = Selector("superview");
            IntPtr bar = Send(buttons[0], superview);
            IntPtr container = bar == IntPtr.Zero ? IntPtr.Zero : Send(bar, superview);

            if (container == IntPtr.Zero)
            {
                return;
            }

            // la fascia di sistema alta quanto la nostra, attaccata al bordo alto della finestra
            Rect frame = Frame(ns);
            Rect strip = Frame(container);
            strip.Height = height;
            strip.Y = frame.Height - height;
            SetFrame(container, Selector("setFrame:"), strip);

            double spacing = Frame(buttons[1]).X - Frame(buttons[0]).X;

            if (spacing <= 0 || spacing > 40)
            {
                spacing = 20;
            }

            IntPtr moveTo = Selector("setFrameOrigin:");

            for (int i = 0; i < buttons.Length; i++)
            {
                Rect button = Frame(buttons[i]);
                SetOrigin(buttons[i], moveTo, new Point { X = left + (i * spacing), Y = Math.Round((height - button.Height) / 2) });
            }
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // senza AppKit raggiungibile i semafori restano dove li mette il sistema
        }
    }

    private static Rect Frame(IntPtr view)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            SendRectStret(out Rect frame, view, Selector("frame"));
            return frame;
        }

        return SendRect(view, Selector("frame"));
    }
}
