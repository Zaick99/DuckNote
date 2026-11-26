using Avalonia;

namespace DuckNote.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}
