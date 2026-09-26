using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using DuckNote.App.Storage;
using DuckNote.App.Theme;
using DuckNote.App.Vault;
using DuckNote.App.Views;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;

namespace DuckNote.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += Rescue;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppSettings settings = AppSettings.Load();
        string theme = settings.FollowSystemTheme ? ThemeTokens.FromSystem() : settings.Theme;

        VaultSession vault = new();
        bool encryptAfterLoad = false;

        if (VaultSession.Exists)
        {
            if (!Unlock(vault, theme))
            {
                Shutdown();
                return;
            }
        }
        else if (!settings.SecurityPrompted)
        {
            settings.SecurityPrompted = true;
            settings.Save();

            Start choice = Offer(vault, theme);
            if (choice == Start.Quit)
            {
                Shutdown();
                return;
            }

            encryptAfterLoad = choice == Start.Encrypted;
        }

        MainWindow window = new(settings, theme, vault, encryptAfterLoad);
        MainWindow = window;
        window.Show();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    private static bool Unlock(VaultSession vault, string theme)
    {
        VaultHeader? header = VaultStore.ReadHeader(AppPaths.Store);
        if (header is null)
        {
            MessageBox.Show(
                "store.bin non e' una cassaforte di DuckNote: potrebbe essere troncato o di un'altra versione.\n\n" +
                "Accanto dovrebbe esserci store.bin.bak: chiudi DuckNote, rinominalo in store.bin e riprova.",
                "DuckNote", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        GateWindow gate = new(GateMode.Unlock, header.Kdf, header.Salt, theme)
        {
            Verify = vault.Unlock
        };

        if (gate.ShowDialog() != true || gate.Outcome.Key is not { } key)
        {
            return false;
        }

        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    private void Rescue(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        bool saved = (MainWindow as MainWindow)?.RescueNote() ?? false;

        MessageBox.Show(
            saved
                ? "DuckNote si e' fermato per un errore imprevisto, ma la nota e' stata salvata.\n\n" +
                  $"Riavvialo: ritroverai il testo dov'era.\n\nDettaglio: {e.Exception.Message}"
                : "DuckNote si e' fermato per un errore imprevisto e non e' riuscito a salvare la nota.\n\n" +
                  $"Prima di riaprirlo, copia store.bin.bak accanto a store.bin.\n\nDettaglio: {e.Exception.Message}",
            "DuckNote", MessageBoxButton.OK, MessageBoxImage.Error);

        Shutdown();
    }

    private enum Start
    {
        Encrypted,

        Plain,

        Quit
    }

    private static Start Offer(VaultSession vault, string theme)
    {
        GateWindow gate = new(GateMode.Create, KdfParameters.Default, null, theme);
        gate.ShowDialog();

        if (gate.Outcome.Abandoned)
        {
            return Start.Quit;
        }

        if (gate.Outcome.Key is not { } key)
        {
            return Start.Plain;
        }

        vault.Pending = new PendingVault(key, gate.Outcome.Kdf!, gate.Outcome.Salt!);
        return Start.Encrypted;
    }
}
