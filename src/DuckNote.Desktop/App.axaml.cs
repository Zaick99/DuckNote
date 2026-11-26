using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DuckNote.App.Scanning;
using DuckNote.App.Storage;
using DuckNote.Desktop.Theme;
using DuckNote.Notes.Settings;

namespace DuckNote.Desktop;

public sealed class App : Application
{
    private OnlyOne? _single;
    private Workspace? _ws;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // i risultati delle scansioni si consegnano sul thread della finestra
            UiThread.Use(() => Dispatcher.UIThread.CheckAccess(), work => Dispatcher.UIThread.InvokeAsync(work).GetTask());

            AppSettings settings = AppSettings.Load();
            Palette.Initialize(settings.ThemePreference);

            _single = OnlyOne.Claim();

            if (_single is null)
            {
                desktop.MainWindow = Views.Message.Window(
                    "DuckNote e' gia' aperto",
                    "Una copia sola per volta: due copie scriverebbero la stessa cassaforte, e chiudere a chiave l'una lascerebbe aperta l'altra.",
                    () => desktop.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _ws = new Workspace(settings);
            MainWindow window = new(_ws)
            {
                Width = Math.Max(980, settings.WindowWidth),
                Height = Math.Max(640, settings.WindowHeight)
            };

            window.Closing += (_, _) =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    settings.WindowWidth = (int)window.Width;
                    settings.WindowHeight = (int)window.Height;
                }
            };

            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                _ws.Shutdown();
                _single.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
