using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DuckNote.App.Dialogs;
using DuckNote.App.Storage;
using DuckNote.App.Theme;
using DuckNote.Notes.Settings;

namespace DuckNote.App;

public partial class App : Application
{
    private OnlyOne? _single;
    private Workspace? _ws;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUiException;
        TaskScheduler.UnobservedTaskException += (_, a) => a.SetObserved();

        // ordine obbligato: token prima di qualunque finestra, perche' gli stili li chiedono con
        // DynamicResource. Vale anche per i dialoghi d'errore piu' sotto.
        AppSettings settings = AppSettings.Load();
        ThemeManager.Initialize(this, settings.ThemePreference);
        Ui.Fx.RegisterPress();
        Ui.Fx.EnableHighFrameRate();

        _single = OnlyOne.Claim();

        if (_single is null)
        {
            DuckDialog.Info(null, "DuckNote e' gia' aperto",
                "Una copia sola per volta: due copie scriverebbero la stessa cassaforte, e chiudere a chiave l'una lascerebbe aperta l'altra.");
            Shutdown();
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
                settings.WindowWidth = (int)window.ActualWidth;
                settings.WindowHeight = (int)window.ActualHeight;
            }
        };

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Nessun crash muto: la nota si salva, poi un dialogo con messaggio, tipo e primo frame del
    /// codice dell'app. Se il dialogo stesso non si costruisce, resta il MessageBox di sistema.
    /// </summary>
    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Exception ex = e.Exception.GetBaseException();
        string ns = typeof(App).Namespace!;
        string where = (ex.StackTrace ?? string.Empty).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains(ns + ".", StringComparison.Ordinal)) ?? string.Empty;
        bool saved = false;

        try
        {
            saved = _ws?.SaveNote(force: true) ?? false;
        }
        catch (Exception)
        {
            saved = false;
        }

        string body = $"{ex.Message}\n\n{ex.GetType().Name}{(where.Length > 0 ? "\n" + where : string.Empty)}\n\n" +
                      (saved ? "La nota e' stata salvata." : "La nota potrebbe non essere stata salvata: prima di riaprire, tieni da parte la copia .bak.");

        try
        {
            DuckDialog.Error(MainWindow, "Errore inatteso", body);
        }
        catch (Exception problem) when (problem is InvalidOperationException or XamlParseException)
        {
            MessageBox.Show(body, "DuckNote", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
