using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.App.Vault;

namespace DuckNote.App.Views;

/// <summary>
/// La schermata di sblocco. Mentre la chiave si forgia gira un indicatore di caricamento. Una
/// password sbagliata si vede, non si legge: la schermata trema, l'etichetta diventa rossa e
/// quello che si era scritto si cancella da solo.
/// </summary>
public partial class LockScreen : UserControl
{
    private Workspace? _ws;
    private bool _working;

    public LockScreen() => InitializeComponent();

    /// <summary>La cassaforte si e' aperta: la finestra puo' scoprirsi.</summary>
    public event Action? Opened;

    public void Attach(Workspace ws) => _ws = ws;

    /// <summary>Gli angoli della finestra (arrotondati, dritti se massimizzata o col vetro di Win10).</summary>
    public CornerRadius Corners
    {
        set => Back.CornerRadius = value;
    }

    public void Present()
    {
        Visibility = Visibility.Visible;
        _working = false;
        Form.Visibility = Visibility.Visible;
        Form.IsEnabled = true;
        Work.Visibility = Visibility.Collapsed;
        Message.Visibility = Visibility.Collapsed;
        Loading.Visibility = Visibility.Collapsed;
        PwdLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
        Pwd.Clear();

        BeginAnimation(OpacityProperty, Motion.Slide(0, 1, 240, Motion.Ease()));
        Fx.Entrance(Stage);
        Dispatcher.BeginInvoke(() => Pwd.Focus(), DispatcherPriority.Input);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOpen(sender, e);
        }
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_working || _ws is null)
        {
            return;
        }

        using SecureString password = Pwd.SecurePassword;

        if (password.Length == 0)
        {
            Fail("Serve la password.");
            return;
        }

        if (VaultSession.Header() is not { } header)
        {
            Fail("store.bin non e' una cassaforte di DuckNote: potrebbe essere troncato o di un'altra versione. Accanto c'e' store.bin.bak.");
            return;
        }

        _working = true;
        Spin(true);

        string cost = Passwords.Cost(header.Kdf);
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(90) };
        tick.Tick += (_, _) => Work.Text = string.Create(CultureInfo.CurrentCulture,
            $"Apertura della cassaforte, {clock.Elapsed.TotalSeconds:N1} s.\n{cost}.");
        tick.Start();

        try
        {
            byte[] key = await VaultSession.DeriveAsync(password, header.Salt, header.Kdf);
            bool opened;

            try
            {
                opened = _ws.Unlock(key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (opened)
            {
                Welcome();
                return;
            }

            Spin(false);
            Deny();
        }
        catch (Exception problem) when (problem is CryptographicException or OutOfMemoryException or System.IO.IOException)
        {
            Spin(false);
            Fail("Apertura non riuscita: " + problem.Message);
        }
        finally
        {
            tick.Stop();
            _working = false;
        }
    }

    private void Spin(bool on)
    {
        Form.IsEnabled = !on;
        Message.Visibility = Visibility.Collapsed;
        Work.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Form.Visibility = on ? Visibility.Hidden : Visibility.Visible;
        Loading.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Deny()
    {
        Fx.Shake(Stage);
        PwdLabel.SetResourceReference(TextBlock.ForegroundProperty, "Red");

        // quel che si era scritto si cancella da se', un carattere alla volta
        DispatcherTimer backspace = new() { Interval = TimeSpan.FromMilliseconds(32) };
        backspace.Tick += (_, _) =>
        {
            string typed = Pwd.Password;

            if (typed.Length == 0)
            {
                backspace.Stop();
                Pwd.Focus();
                return;
            }

            Pwd.Password = typed[..^1];
        };
        backspace.Start();

        DispatcherTimer calm = new() { Interval = TimeSpan.FromMilliseconds(900) };
        calm.Tick += (_, _) =>
        {
            calm.Stop();
            PwdLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");
        };
        calm.Start();
    }

    private void Fail(string message)
    {
        Message.Text = message;
        Message.Visibility = Visibility.Visible;
        Fx.Shake(Stage);
        Pwd.Clear();
        Pwd.Focus();
    }

    /// <summary>Aperta: il caricamento si ferma e la schermata svanisce.</summary>
    private void Welcome()
    {
        Work.Text = "Apertura della nota...";

        DoubleAnimation fade = Motion.Slide(1, 0, 260, Motion.Ease(mode: EasingMode.EaseIn));
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            Loading.Visibility = Visibility.Collapsed;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        BeginAnimation(OpacityProperty, fade);
        Opened?.Invoke();
    }
}
