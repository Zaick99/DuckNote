using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DuckNote.App.Dialogs;
using DuckNote.App.Ui;
using DuckNote.App.Vault;
using DuckNote.Core.Crypto;

namespace DuckNote.App.Pages;

/// <summary>
/// La cassaforte: senza cifratura e' una procedura in tre passi (perche', password, conferma);
/// con la cifratura attiva mostra lo stato, il blocco automatico e i comandi.
/// </summary>
public partial class VaultPage : UserControl, IDuckPage
{
    private readonly Workspace _ws;
    private readonly Wizard _wizard;
    private bool _forging;

    public VaultPage(Workspace ws)
    {
        _ws = ws;
        InitializeComponent();

        _wizard = new Wizard(Stepper,
        [
            new WizardStep("Perche'", StepWhy),
            new WizardStep("Password", StepPassword),
            new WizardStep("Conferma", StepConfirm)
        ]);
        _wizard.Changed += OnStep;
        _wizard.CanLeave = CanLeave;

        CostText.Text = Passwords.Cost(KdfParameters.Default);
        SumCost.Text = Passwords.Cost(KdfParameters.Default);

        ws.VaultChanged += () => Dispatcher.BeginInvoke(ShowState);
        ShowState();
        _wizard.Go(0, animate: false);
    }

    /// <summary>Come sta la cassaforte, in una riga: le Impostazioni la mostrano sotto il titolo.</summary>
    public string Summary => HeadSub.Text;

    public event Action? SummaryChanged;

    /// <summary>Ospitata dalle Impostazioni: niente intestazione propria, niente margini.</summary>
    public bool Embedded
    {
        set
        {
            Head.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            Root.Margin = value ? new Thickness(0) : new Thickness(20, 2, 20, 16);
        }
    }

    public void OnShown()
    {
        ShowState();

        if (!_ws.Vault.IsOpen && _wizard.Index == 1)
        {
            Pwd.Focus();
        }
    }

    /// <summary>Il primo avvio: la procedura si propone da sola, con "Piu' tardi" per rinunciare.</summary>
    public void Offer()
    {
        ShowState();
        _wizard.Go(0, animate: false);
    }

    private void ShowState()
    {
        bool on = _ws.Vault.IsOpen;

        PanelOn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PanelOff.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        HeadSub.Text = on
            ? "La nota e' cifrata. Bloccala quando ti allontani, o lascia che lo faccia da sola."
            : "La nota e' in chiaro su disco. Una password la chiude a chiave.";
        SummaryChanged?.Invoke();
        LockMinutes.Text = _ws.Settings.AutoLockMinutes.ToString(CultureInfo.CurrentCulture);
    }

    // ------------------------------------------------------------------ procedura

    private void OnStep(int index)
    {
        BtnBack.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
        BtnLater.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtNext.Text = _wizard.IsLast ? "Chiudi a chiave" : "Avanti";
        Forging.Visibility = Visibility.Collapsed;
        PwdProblem.Visibility = Visibility.Collapsed;

        if (index == 1)
        {
            Dispatcher.BeginInvoke(() => Pwd.Focus(), DispatcherPriority.Input);
        }
    }

    private bool CanLeave(int index)
    {
        if (index != 1)
        {
            return true;
        }

        using SecureString first = Pwd.SecurePassword;
        using SecureString second = Pwd2.SecurePassword;

        if (first.Length < Passwords.Shortest)
        {
            Problem("Almeno otto caratteri: sotto questa soglia la chiave non protegge nulla.", Pwd);
            return false;
        }

        if (!Passwords.Same(first, second))
        {
            Problem("Le due password non coincidono.", Pwd2);
            Pwd2.Clear();
            return false;
        }

        return true;
    }

    private void Problem(string message, Control field)
    {
        PwdProblemText.Text = message;
        PwdProblem.Visibility = Visibility.Visible;
        Fx.Shake(field);
    }

    private void OnPwdChanged(object sender, RoutedEventArgs e)
    {
        if (Strength is null)
        {
            return;
        }

        using SecureString typed = Pwd.SecurePassword;
        Strength.Show(typed);
        PwdProblem.Visibility = Visibility.Collapsed;
    }

    private void OnPwdKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnNext(sender, e);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (!_forging)
        {
            _wizard.Back();
        }
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        _ws.Settings.SecurityPrompted = true;
        _ws.Settings.Save();
        _ws.Say("La nota resta in chiaro. Puoi chiuderla a chiave quando vuoi, da Impostazioni > Cassaforte.");

        if (Window.GetWindow(this) is MainWindow shell)
        {
            shell.Navigate("notes");
        }
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_forging)
        {
            return;
        }

        if (!_wizard.IsLast)
        {
            _wizard.Next();
            return;
        }

        if (Understood.IsChecked != true)
        {
            Fx.Shake(Understood);
            return;
        }

        await Forge();
    }

    /// <summary>Forgia la chiave fuori dal thread della finestra e crea la cassaforte con la nota dentro.</summary>
    private async Task Forge()
    {
        _forging = true;
        BtnNext.IsEnabled = false;
        BtnBack.IsEnabled = false;
        Forging.Visibility = Visibility.Visible;

        byte[] salt = VaultSession.FreshSalt();
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
        tick.Tick += (_, _) => ForgingText.Text = string.Create(CultureInfo.CurrentCulture,
            $"Forgiatura della chiave, {clock.Elapsed.TotalSeconds:N1} s. {Passwords.Cost(KdfParameters.Default)}.");
        tick.Start();

        try
        {
            byte[] key;

            using (SecureString password = Pwd.SecurePassword)
            {
                key = await VaultSession.DeriveAsync(password, salt, KdfParameters.Default);
            }

            try
            {
                _ws.Encrypt(key, KdfParameters.Default, salt);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            Pwd.Clear();
            Pwd2.Clear();
            _wizard.Finish(StepDone);
            BtnLater.Visibility = Visibility.Collapsed;
            NavBar.Visibility = Visibility.Collapsed;
            Fx.Celebrate(DoneArt);
            Toast.Success("Cifratura attiva", "La nota vive nel contenitore: i file in chiaro sono stati rimossi.");

            DispatcherTimer back = new() { Interval = TimeSpan.FromSeconds(2.4) };
            back.Tick += (_, _) =>
            {
                back.Stop();
                NavBar.Visibility = Visibility.Visible;
                _wizard.Go(0, animate: false);
                ShowState();
            };
            back.Start();
        }
        catch (Exception problem) when (problem is CryptographicException or System.IO.IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            DuckDialog.Error(Window.GetWindow(this), "Cifratura non riuscita", problem.Message);
        }
        finally
        {
            tick.Stop();
            _forging = false;
            BtnNext.IsEnabled = true;
            BtnBack.IsEnabled = true;
            Forging.Visibility = Visibility.Collapsed;
        }
    }

    // ------------------------------------------------------------------ cassaforte aperta

    private void OnLockNow(object sender, RoutedEventArgs e) => _ws.Lock("a mano");

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        if (Views.PasswordDialog.Ask(Window.GetWindow(this)) is not { } forged)
        {
            return;
        }

        try
        {
            _ws.ChangePassword(forged.Key, forged.Kdf, forged.Salt);
            Toast.Success("Password cambiata", "La cassaforte si apre con la nuova password.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(forged.Key);
        }
    }

    private void OnDecrypt(object sender, RoutedEventArgs e)
    {
        if (!DuckDialog.Confirm(Window.GetWindow(this), "Togliere la cifratura?",
                "La nota torna in chiaro su disco e il contenitore viene coperto e rimosso.", "Togli la cifratura", danger: true))
        {
            return;
        }

        if (_ws.Decrypt())
        {
            Toast.Warning("Cifratura tolta", "La nota e' di nuovo in chiaro su disco.");
            ShowState();
            _wizard.Go(0, animate: false);
            return;
        }

        Toast.Error("Non riuscito", "Il contenitore resta com'era: la nota e' al sicuro.");
    }

    private void OnLockMinutes(object sender, RoutedEventArgs e)
    {
        int minutes = int.TryParse(LockMinutes.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int typed)
            ? Math.Clamp(typed, 0, 1440)
            : _ws.Settings.AutoLockMinutes;

        LockMinutes.Text = minutes.ToString(CultureInfo.CurrentCulture);

        if (minutes != _ws.Settings.AutoLockMinutes)
        {
            _ws.Settings.AutoLockMinutes = minutes;
            _ws.Settings.Save();
            _ws.StartTimers();
            _ws.Say(minutes == 0 ? "Blocco automatico spento." : $"Blocco automatico dopo {minutes} minuti fermi.");
        }
    }
}
