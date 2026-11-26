using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DuckNote.App.Vault;
using DuckNote.Core.Crypto;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Security;

namespace DuckNote.Desktop.Views;

/// <summary>La chiave nuova, forgiata: chi la riceve la azzera dopo averla usata.</summary>
public sealed record ForgedKey(byte[] Key, KdfParameters Kdf, byte[] Salt);

/// <summary>Da una casella di testo a una SecureString, che chi la riceve chiude.</summary>
public static class Secret
{
    public static SecureString From(TextBox box)
    {
        SecureString secret = new();

        foreach (char c in box.Text ?? string.Empty)
        {
            secret.AppendChar(c);
        }

        secret.MakeReadOnly();
        return secret;
    }

    /// <summary>Una casella per la password: punti al posto dei caratteri, niente menu Copia.</summary>
    public static TextBox Field()
    {
        TextBox box = new() { PasswordChar = '●', Height = 36, FontSize = 14 };
        box.ContextMenu = null;
        box.ContextFlyout = null;
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // niente copia della password: Ctrl/Cmd+C e Ctrl/Cmd+X non escono dalla casella
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && e.Key is Key.C or Key.X)
            {
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return box;
    }
}

/// <summary>
/// La barra della robustezza sotto la casella della password: lunghezza per alfabeto. Misura la
/// lunghezza, non l'imprevedibilita' (lo dice la riga sotto).
/// </summary>
public sealed class StrengthBar : StackPanel
{
    private const string Hint = "Quattro parole tirate a sorte valgono piu' di una parola con simboli.";

    private readonly Border _track = new() { Height = 5, CornerRadius = new CornerRadius(3) };
    private readonly Border _fill = new() { Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly TextBlock _words = new() { Text = Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };

    public StrengthBar()
    {
        _track.Bind(Border.BackgroundProperty, _track.GetResourceObservable("BgPressed"));
        _track.Child = _fill;
        _words.Classes.Add("caption");
        Children.Add(_track);
        Children.Add(_words);
    }

    public void Show(TextBox box)
    {
        using SecureString typed = Secret.From(box);
        int bits = Passwords.Bits(typed);
        (string token, string word) = PasswordStrength.Describe(bits);

        _fill.Bind(Border.BackgroundProperty, _fill.GetResourceObservable(token));
        double full = Math.Max(60, _track.Bounds.Width);
        double from = double.IsNaN(_fill.Width) ? 0 : _fill.Width;
        double to = full * Math.Min(1, bits / 110.0);
        Motion.Tween((_fill, "width"), from, to, 260, w => _fill.Width = w);
        _words.Text = bits == 0
            ? Hint
            : $"{bits} bit, {word}. Misura la lunghezza, non l'imprevedibilita': usa i dadi, non l'ispirazione.";
    }
}

/// <summary>Una password nuova, ripetuta, misurata e trasformata in chiave fuori dal thread della finestra.</summary>
public static class PasswordDialog
{
    public static async Task<ForgedKey?> Ask(Window owner)
    {
        ForgedKey? forged = null;
        bool busy = false;

        Window window = Dialog.Make("Cambia password", 460);
        TextBox first = Secret.Field();
        TextBox second = Secret.Field();
        StrengthBar strength = new() { Margin = new Thickness(2, 8, 0, 0) };
        TextBlock problem = Dialog.Tinted(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), IsVisible = false }, "Red");
        TextBlock work = Dialog.Tinted(new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }, "LabelSecondary");
        StackPanel forging = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 14, 0, 0),
            IsVisible = false,
            Children = { new ProgressBar { IsIndeterminate = true, Width = 90, VerticalAlignment = VerticalAlignment.Center }, work }
        };

        Button cancel = Dialog.Button("Annulla", "Quiet", window.Close);
        Button ok = Dialog.Button("Cambia password", "Primary", () => { });

        void Complain(string message, TextBox field)
        {
            problem.Text = message;
            problem.IsVisible = true;
            field.Focus();
        }

        async Task Confirm()
        {
            if (busy)
            {
                return;
            }

            using SecureString one = Secret.From(first);
            using SecureString two = Secret.From(second);

            if (one.Length < Passwords.Shortest)
            {
                Complain("Almeno otto caratteri: sotto questa soglia la chiave non protegge nulla.", first);
                return;
            }

            if (!Passwords.Same(one, two))
            {
                second.Text = string.Empty;
                Complain("Le due password non coincidono.", second);
                return;
            }

            busy = true;
            ok.IsEnabled = cancel.IsEnabled = first.IsEnabled = second.IsEnabled = false;
            problem.IsVisible = false;
            forging.IsVisible = true;

            byte[] salt = VaultSession.FreshSalt();
            Stopwatch clock = Stopwatch.StartNew();
            DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
            tick.Tick += (_, _) => work.Text = string.Create(CultureInfo.CurrentCulture, $"Forgiatura della chiave, {clock.Elapsed.TotalSeconds:N1} s.");
            tick.Start();

            try
            {
                byte[] key = await VaultSession.DeriveAsync(one, salt, KdfParameters.Default);
                forged = new ForgedKey(key, KdfParameters.Default, salt);
                window.Close();
            }
            catch (Exception failure) when (failure is CryptographicException or OutOfMemoryException)
            {
                ok.IsEnabled = cancel.IsEnabled = first.IsEnabled = second.IsEnabled = true;
                busy = false;
                Complain("Derivazione non riuscita: " + failure.Message, first);
            }
            finally
            {
                tick.Stop();
                forging.IsVisible = false;
            }
        }

        ok.Click += async (_, _) => await Confirm();
        second.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await Confirm();
            }
        };
        first.TextChanged += (_, _) =>
        {
            strength.Show(first);
            problem.IsVisible = false;
        };
        window.Closing += (_, e) =>
        {
            // durante la forgiatura la finestra resta: la chiave a meta' non va lasciata in giro
            if (busy && forged is null)
            {
                e.Cancel = true;
            }
        };
        window.Closed += (_, _) =>
        {
            first.Text = string.Empty;
            second.Text = string.Empty;
        };

        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children =
            {
                Dialog.Heading("Cambia password"),
                Dialog.Caption("La cassaforte si aprira' con la nuova password. La nota non si riscrive: cambia solo la chiave che apre la chiave vera."),
                Dialog.Label("NUOVA PASSWORD"),
                first,
                strength,
                Dialog.Label("RIPETI LA PASSWORD"),
                second,
                problem,
                forging,
                Dialog.Buttons(cancel, ok)
            }
        };
        window.Opened += (_, _) => first.Focus();

        await window.ShowDialog(owner);
        return forged;
    }
}
