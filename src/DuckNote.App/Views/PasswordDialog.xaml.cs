using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.App.Vault;
using DuckNote.Core.Crypto;

namespace DuckNote.App.Views;

/// <summary>La chiave nuova, forgiata: chi la riceve la azzera dopo averla usata.</summary>
public sealed record ForgedKey(byte[] Key, KdfParameters Kdf, byte[] Salt);

/// <summary>Una password nuova, ripetuta, misurata e trasformata in chiave fuori dal thread della finestra.</summary>
public partial class PasswordDialog : Window
{
    private ForgedKey? _forged;
    private bool _busy;

    private PasswordDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Motion.OpenDialog(Root);
            Pwd.Focus();
        };

        Closed += (_, _) =>
        {
            Pwd.Clear();
            Pwd2.Clear();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
    }

    public static ForgedKey? Ask(Window? owner)
    {
        PasswordDialog dialog = new();

        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog._forged : null;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (Strength is null)
        {
            return;
        }

        using SecureString typed = Pwd.SecurePassword;
        Strength.Show(typed);
        Problem.Visibility = Visibility.Collapsed;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOk(sender, e);
        }
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        using SecureString first = Pwd.SecurePassword;
        using SecureString second = Pwd2.SecurePassword;

        if (first.Length < Passwords.Shortest)
        {
            Complain("Almeno otto caratteri: sotto questa soglia la chiave non protegge nulla.");
            return;
        }

        if (!Passwords.Same(first, second))
        {
            Complain("Le due password non coincidono.");
            Pwd2.Clear();
            return;
        }

        _busy = true;
        BtnOk.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        Form.IsEnabled = false;
        Forging.Visibility = Visibility.Visible;

        byte[] salt = VaultSession.FreshSalt();
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
        tick.Tick += (_, _) => ForgingText.Text = string.Create(CultureInfo.CurrentCulture,
            $"Forgiatura della chiave, {clock.Elapsed.TotalSeconds:N1} s.");
        tick.Start();

        try
        {
            byte[] key = await VaultSession.DeriveAsync(first, salt, KdfParameters.Default);
            _forged = new ForgedKey(key, KdfParameters.Default, salt);
            Motion.CloseDialog(this, Root, true);
        }
        catch (Exception problem) when (problem is System.Security.Cryptography.CryptographicException or OutOfMemoryException)
        {
            Complain("Derivazione non riuscita: " + problem.Message);
            Form.IsEnabled = true;
            BtnOk.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            _busy = false;
        }
        finally
        {
            tick.Stop();
            Forging.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            Motion.CloseDialog(this, Root, false);
        }
    }

    private void Complain(string message)
    {
        ProblemText.Text = message;
        Problem.Visibility = Visibility.Visible;
        Fx.Shake(Pwd);
    }
}
