using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DuckNote.App.Vault;
using DuckNote.Desktop.Ui;

namespace DuckNote.Desktop.Views;

/// <summary>
/// La schermata di sblocco: copre tutta la finestra con un colore solo. Mentre la chiave si
/// forgia (Argon2id, qualche secondo) gira un indicatore; una password sbagliata si vede, non si
/// legge: la scheda trema, l'etichetta diventa rossa e quello che si era scritto si cancella.
/// </summary>
public sealed class LockScreen : UserControl
{
    private readonly TextBox _password = new() { PasswordChar = '●', PlaceholderText = "Password", Width = 280, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { Text = "PASSWORD", FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 6) };
    private readonly TextBlock _message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, TextAlignment = TextAlignment.Center, IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _work = new() { FontSize = 12, TextAlignment = TextAlignment.Center, IsVisible = false, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _open = new() { Content = "Apri", Width = 120, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    // un caricamento classico: la barra che scorre mentre la chiave si forgia
    private readonly ProgressBar _spinner = new() { IsIndeterminate = true, Width = 220, Height = 4, MinHeight = 4, IsVisible = false, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

    private Workspace? _ws;
    private bool _working;

    public LockScreen()
    {
        Border back = new();
        back.Bind(Border.BackgroundProperty, back.GetResourceObservable("BgWindow"));

        Image art = new() { Width = 118, Height = 118, Margin = new Thickness(0, 0, 0, 14) };

        try
        {
            art.Source = new Bitmap(AssetLoader.Open(new Uri("avares://DuckNote/Assets/art/lock.png")));
        }
        catch (Exception problem) when (problem is IOException or FileNotFoundException)
        {
            art.IsVisible = false;
        }

        TextBlock title = Dialog.Tinted(new TextBlock { Text = "La nota e' chiusa a chiave", FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center }, "Label");
        TextBlock sub = Dialog.Tinted(new TextBlock { Text = "Serve la password per aprirla.", FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) }, "LabelSecondary");
        Dialog.Tinted(_label, "LabelTertiary");
        Dialog.Tinted(_message, "Red");
        Dialog.Tinted(_work, "LabelSecondary");
        _spinner.Bind(ProgressBar.ForegroundProperty, _spinner.GetResourceObservable("Accent"));
        _open.Theme = Dialog.Theme("Primary");

        _stage.Children.AddRange([art, title, sub, _label, _password, _open, _spinner, _work, _message]);
        _stage.RenderTransform = new TranslateTransform();

        Content = new Panel { Children = { back, _stage } };

        _password.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnOpen();
                e.Handled = true;
            }
        };
        _open.Click += (_, _) => OnOpen();
    }

    /// <summary>La cassaforte si e' aperta: la finestra puo' scoprirsi.</summary>
    public event Action? Opened;

    public void Attach(Workspace ws) => _ws = ws;

    public void Present()
    {
        IsVisible = true;
        _working = false;
        _password.IsEnabled = true;
        _open.IsEnabled = true;
        _work.IsVisible = false;
        _message.IsVisible = false;
        Spin(false);
        Dialog.Tinted(_label, "LabelTertiary");
        _password.Text = string.Empty;
        Motion.Tween((this, "fade"), 0, 1, 260, t => Opacity = t);
        Dispatcher.UIThread.Post(() => _password.Focus(), DispatcherPriority.Input);
    }

    private async void OnOpen()
    {
        if (_working || _ws is null)
        {
            return;
        }

        string typed = _password.Text ?? string.Empty;

        if (typed.Length == 0)
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

        using SecureString password = new();

        foreach (char c in typed)
        {
            password.AppendChar(c);
        }

        password.MakeReadOnly();
        string cost = Passwords.Cost(header.Kdf);
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(90) };
        tick.Tick += (_, _) => _work.Text = string.Create(CultureInfo.CurrentCulture, $"Apertura della cassaforte, {clock.Elapsed.TotalSeconds:N1} s.\n{cost}.");
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
        catch (Exception problem) when (problem is CryptographicException or OutOfMemoryException or IOException)
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
        _password.IsEnabled = !on;
        _open.IsEnabled = !on;
        _message.IsVisible = false;
        _work.IsVisible = on;
        _spinner.IsVisible = on;
    }

    private void Deny()
    {
        Shake();
        Dialog.Tinted(_label, "Red");

        DispatcherTimer backspace = new() { Interval = TimeSpan.FromMilliseconds(32) };
        backspace.Tick += (_, _) =>
        {
            string typed = _password.Text ?? string.Empty;

            if (typed.Length == 0)
            {
                backspace.Stop();
                _password.Focus();
                return;
            }

            _password.Text = typed[..^1];
        };
        backspace.Start();

        DispatcherTimer calm = new() { Interval = TimeSpan.FromMilliseconds(900) };
        calm.Tick += (_, _) =>
        {
            calm.Stop();
            Dialog.Tinted(_label, "LabelTertiary");
        };
        calm.Start();
    }

    private void Fail(string message)
    {
        _message.Text = message;
        _message.IsVisible = true;
        Shake();
        _password.Text = string.Empty;
        _password.Focus();
    }

    private void Shake()
    {
        if (_stage.RenderTransform is not TranslateTransform shift)
        {
            return;
        }

        Motion.Tween((_stage, "shake"), 0, 1, 420, t => shift.X = Math.Sin(t * Math.PI * 6) * 9 * (1 - t), () => shift.X = 0);
    }

    /// <summary>Aperta: la schermata svanisce.</summary>
    private void Welcome()
    {
        _work.Text = "Apertura della nota...";
        Motion.Tween((this, "fade"), Opacity, 0, 260, t => Opacity = t, () =>
        {
            IsVisible = false;
            Opacity = 1;
            Spin(false);
        });
        Opened?.Invoke();
    }
}
