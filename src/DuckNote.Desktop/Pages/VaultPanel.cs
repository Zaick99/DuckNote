using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DuckNote.App.Vault;
using DuckNote.Core.Crypto;
using DuckNote.Desktop.Ui;
using DuckNote.Desktop.Views;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// La cassaforte, scheda delle Impostazioni come nella versione WPF: senza cifratura e' una
/// procedura in tre passi (perche', password, conferma); con la cifratura attiva mostra lo stato,
/// il blocco automatico e i comandi.
/// </summary>
public sealed class VaultPanel : UserControl
{
    private static readonly string[] StepNames = ["Perche'", "Password", "Conferma"];

    private readonly Workspace _ws;
    private readonly Func<Task> _flush;
    private readonly Func<Task> _lock;
    private readonly Action _later;

    private readonly ScrollViewer _on;
    private readonly TextBox _lockMinutes = Kit.Number(70, "15");

    private readonly Grid _off;
    private readonly StackPanel _stepper = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Control[] _steps;
    private readonly Control _done;
    private int _index;

    private readonly TextBox _pwd = Secret.Field();
    private readonly TextBox _pwd2 = Secret.Field();
    private readonly StrengthBar _strength = new() { Margin = new Thickness(2, 8, 0, 0) };
    private readonly Border _pwdProblem;
    private readonly TextBlock _pwdProblemText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly OnOff _understood;
    private readonly StackPanel _forging;
    private readonly TextBlock _forgingText = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
    private readonly Grid _nav;
    private readonly Button _back;
    private readonly Button _laterButton;
    private readonly Button _next;
    private readonly TextBlock _nextText = new() { Text = "Avanti", VerticalAlignment = VerticalAlignment.Center };
    private bool _working;

    public VaultPanel(Workspace ws, Func<Task> flush, Func<Task> lockNow, Action later)
    {
        _ws = ws;
        _flush = flush;
        _lock = lockNow;
        _later = later;

        // ------------------------------------------------------------ cifratura attiva
        _lockMinutes.LostFocus += (_, _) => OnLockMinutes();
        _lockMinutes.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnLockMinutes();
            }
        };

        Button lockButton = Kit.Labelled("Primary", "lock", "Blocca adesso");
        lockButton.Click += async (_, _) => await _lock();
        Button change = Kit.Labelled("Soft", "key", "Cambia password");
        change.Margin = new Thickness(8, 0, 0, 0);
        change.Click += async (_, _) => await OnChangePassword();
        Button decrypt = new() { Theme = Themes.Of("Danger"), Content = "Togli la cifratura..." };
        decrypt.Click += async (_, _) => await OnDecrypt();

        string lockKey = OperatingSystem.IsMacOS() ? "Cmd+L" : "Ctrl+L";
        StackPanel left = Kit.Stack(
            Kit.Header("Stato", first: true),
            Kit.Group(
                Kit.Row("Cifratura", "La nota e tutto quello che le sta intorno vivono in store.bin.", Kit.Chip("ATTIVA, APERTA", "Green")),
                Kit.Row("Chiave", Passwords.Cost(KdfParameters.Default), null),
                Kit.Row("Blocca dopo (minuti di inattivita')", $"0 non blocca mai. {lockKey} blocca subito.", _lockMinutes),
                Kit.Block("Chiudi la cassaforte", "La nota si salva, la chiave lascia la memoria e serve di nuovo la password.",
                    new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { lockButton, change } })),
            Kit.Header("Zona pericolosa"),
            Danger(Kit.Group(Kit.Row("Togli la cifratura", "La nota torna in chiaro su disco e il contenitore viene distrutto.", decrypt))));

        StackPanel right = Kit.Stack(
            Kit.Header("Cosa protegge", first: true),
            Kit.Box("shield", "Green",
                "Nota, nota precedente, ultima scansione e host messi da parte stanno tutti in store.bin, cifrati con AES-256 e autenticati con HMAC-SHA256. Il contenuto e' riempito a passi di 4 KiB, cosi' la misura del file non dice quanto hai scritto. Mentre la cassaforte esiste nulla viene scritto in chiaro."),
            Kit.Header("Cosa non protegge"),
            Kit.Box("warning", "Red",
                "Un keylogger o un malware nel tuo account, un debugger su DuckNote aperto, un eseguibile sostituito, lo schermo guardato, e le copie in chiaro fatte prima di attivare la cifratura (istantanee di Time Machine o del disco, cartelle sincronizzate). Se perdi la password la nota e' persa: non c'e' recupero."));

        _on = new ScrollViewer { Content = Kit.Columns(left, right), IsVisible = false };

        // ------------------------------------------------------------ procedura
        _pwd.TextChanged += (_, _) =>
        {
            _strength.Show(_pwd);
            _pwdProblem!.IsVisible = false;
        };
        _pwd.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _pwd2.Focus();
            }
        };
        _pwd2.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OnNext();
            }
        };

        _pwdProblemText.Bind(TextBlock.ForegroundProperty, _pwdProblemText.GetResourceObservable("Red"));
        _pwdProblem = new Border { Child = _pwdProblemText, Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
        _pwdProblem.Bind(Border.BackgroundProperty, _pwdProblem.GetResourceObservable("RedSoft"));

        _understood = Kit.Switch(false, _ => { });
        _forgingText.Classes.Add("muted");
        _forging = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 18, 0, 0),
            IsVisible = false,
            Children = { new WaitRing { Width = 26, Height = 26, Thickness = 3.5, VerticalAlignment = VerticalAlignment.Center }, _forgingText }
        };

        Control why = Step("art/lock.png", "Chiudi la nota a chiave",
            "Una password trasforma nota, copia precedente, ultima scansione e host messi da parte in un solo contenitore cifrato.",
            Kit.Header("Come funziona"),
            Kit.Group(
                Kit.IconRow("key", "Accent", "La password non diventa la chiave",
                    "Argon2id (256 MiB) e PBKDF2 ne ricavano una chiave che apre soltanto la chiave vera, casuale. Cambiare password riscrive cento byte, non la nota."),
                Kit.IconRow("lock", "Accent", "La chiave non resta in giro",
                    "In memoria sta fuori dal garbage collector, bloccata fuori dallo swap e cifrata quando non serve. Dopo 15 minuti fermi se ne va."),
                Kit.IconRow("warning", "Red", "Nessun recupero",
                    "Non c'e' una chiave di riserva ne' una porta di servizio: e' proprio questo il punto. Se perdi la password, la nota e' persa.", "Red")));

        Control password = Step("art/password.png", "Scegli la password",
            "Almeno otto caratteri. Contro cento GPU da datacenter quattro parole tirate a sorte reggono secoli; 'Estate2026!' qualche minuto.",
            Kit.Header("Password"),
            Kit.Group(new StackPanel
            {
                Margin = new Thickness(16, 14),
                Children =
                {
                    Kit.Texts("Nuova password", null),
                    Spaced(_pwd, 8),
                    _strength,
                    Spaced(Kit.Sep(), 14, 14),
                    Kit.Texts("Ripeti la password", null),
                    Spaced(_pwd2, 8)
                }
            }),
            _pwdProblem);

        TextBlock cipher = new() { Text = "AES-256 + HMAC-SHA256", FontFamily = Mono, FontSize = 11.5 };
        cipher.Classes.Add("caption");
        Control confirm = Step("art/vault.png", "Pronto",
            "La chiave si forgia in qualche secondo: e' lento apposta, e lo sara' per chiunque provi a indovinare.",
            Kit.Header("Riepilogo"),
            Kit.Group(
                Kit.Row("Derivazione", Passwords.Cost(KdfParameters.Default), null),
                Kit.Row("Contenuto", null, cipher),
                Kit.Row("Ho capito: se perdo la password la nota e' persa", "Serve la conferma per continuare.", _understood, "Red")),
            _forging);

        TextBlock doneTitle = new() { Text = "La nota e' chiusa a chiave", FontSize = 24, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        doneTitle.Bind(TextBlock.ForegroundProperty, doneTitle.GetResourceObservable("Label"));
        TextBlock doneText = new()
        {
            Text = "Da ora nulla viene scritto in chiaro, e i file in chiaro di prima sono stati coperti e rimossi. Ogni avvio passera' dalla schermata di sblocco.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        doneText.Classes.Add("muted");
        _done = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false,
            Children = { Art.Picture("art/done.png", 160), doneTitle, doneText }
        };

        _steps = [why, password, confirm];
        Grid stepHost = new() { ClipToBounds = true };
        stepHost.Children.AddRange(_steps);
        stepHost.Children.Add(_done);

        _back = Kit.Labelled("Quiet", "left", "Indietro", 34);
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        _back.Click += (_, _) =>
        {
            if (!_working && _index > 0)
            {
                Go(_index - 1);
            }
        };
        _laterButton = new Button { Theme = Themes.Of("Quiet"), Height = 34, Content = "Piu' tardi", Margin = new Thickness(0, 0, 8, 0) };
        _laterButton.Click += (_, _) => OnLater();
        _next = new Button
        {
            Theme = Themes.Of("Primary"),
            Height = 34,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _nextText, new Glyph { Kind = "right", Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center } } }
        };
        _next.Click += async (_, _) => await OnNext();

        _nav = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        _nav.Children.Add(_back);
        _nav.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _laterButton, _next } });

        Border stepperWell = new() { Padding = new Thickness(14, 8), Margin = new Thickness(0, 6, 0, 16), HorizontalAlignment = HorizontalAlignment.Center, Child = _stepper };
        stepperWell.Classes.Add("well");

        _off = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), MaxWidth = 780 };
        _off.Children.Add(stepperWell);
        Grid.SetRow(stepHost, 1);
        _off.Children.Add(stepHost);
        Grid.SetRow(_nav, 2);
        _off.Children.Add(_nav);

        Content = new Grid { Children = { _on, _off } };

        ws.VaultChanged += () => Dispatcher.UIThread.Post(ShowState);
        ShowState();
        Go(0, animate: false);
    }

    /// <summary>Come sta la cassaforte, in una riga: le Impostazioni la mostrano sotto il titolo.</summary>
    public string Summary { get; private set; } = string.Empty;

    public event Action? SummaryChanged;

    public void OnShown()
    {
        ShowState();

        if (!_ws.Vault.IsOpen && _index == 1)
        {
            Dispatcher.UIThread.Post(() => _pwd.Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>Il primo avvio: la procedura si propone da sola, con "Piu' tardi" per rinunciare.</summary>
    public void Offer()
    {
        ShowState();
        Go(0, animate: false);
    }

    private void ShowState()
    {
        bool on = _ws.Vault.IsOpen;
        _on.IsVisible = on;
        _off.IsVisible = !on;
        Summary = on
            ? "La nota e' cifrata. Bloccala quando ti allontani, o lascia che lo faccia da sola."
            : "La nota e' in chiaro su disco. Una password la chiude a chiave.";
        SummaryChanged?.Invoke();
        _lockMinutes.Text = _ws.Settings.AutoLockMinutes.ToString(CultureInfo.CurrentCulture);
    }

    // ------------------------------------------------------------------ procedura

    private void Go(int index, bool animate = true)
    {
        int from = _index;
        _index = index;

        for (int i = 0; i < _steps.Length; i++)
        {
            _steps[i].IsVisible = i == index;
        }

        _done.IsVisible = false;

        if (animate && from != index)
        {
            Motion.Show(_steps[index], rise: 0, ms: 240);
            Slide(_steps[index], index > from ? 26 : -26);
        }

        _back.IsVisible = index > 0;
        _laterButton.IsVisible = index == 0;
        _nextText.Text = index == _steps.Length - 1 ? "Chiudi a chiave" : "Avanti";
        _forging.IsVisible = false;
        _pwdProblem.IsVisible = false;
        ShowStepper();

        if (index == 1)
        {
            Dispatcher.UIThread.Post(() => _pwd.Focus(), DispatcherPriority.Input);
        }
    }

    private static void Slide(Control step, double from)
    {
        TranslateTransform shift = step.RenderTransform as TranslateTransform ?? new TranslateTransform();
        step.RenderTransform = shift;
        Motion.Tween((step, "slide"), from, 0, 260, x => shift.X = x, null, Curves.CubicOut);
    }

    /// <summary>I passi in alto: fatti in verde con la spunta, il corrente d'accento, i prossimi grigi.</summary>
    private void ShowStepper()
    {
        _stepper.Children.Clear();

        for (int i = 0; i < StepNames.Length; i++)
        {
            if (i > 0)
            {
                Border line = new() { Width = 26, Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center };
                line.Bind(Border.BackgroundProperty, line.GetResourceObservable(i <= _index ? "Accent" : "Separator"));
                _stepper.Children.Add(line);
            }

            bool done = i < _index;
            bool current = i == _index;
            string tone = done ? "Green" : current ? "Accent" : "LabelTertiary";

            Control mark = done
                ? new Glyph { Kind = "check", Width = 10, Height = 10, Thickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            if (mark is Glyph glyph)
            {
                glyph.Bind(Glyph.ForegroundProperty, glyph.GetResourceObservable(done ? "Green" : "AccentText"));
            }
            else if (mark is TextBlock number)
            {
                number.Bind(TextBlock.ForegroundProperty, number.GetResourceObservable(current ? "AccentText" : "LabelTertiary"));
            }

            Border dot = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Child = mark };
            dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(done ? "GreenSoft" : current ? "Accent" : "BgPressed"));

            TextBlock name = new() { Text = StepNames[i], FontSize = 12, FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable(current ? "Label" : tone == "Green" ? "LabelSecondary" : "LabelTertiary"));

            _stepper.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { dot, name } });
        }
    }

    private bool CanLeave()
    {
        if (_index != 1)
        {
            return true;
        }

        using SecureString first = Secret.From(_pwd);
        using SecureString second = Secret.From(_pwd2);

        if (first.Length < Passwords.Shortest)
        {
            Problem("Almeno otto caratteri: sotto questa soglia la chiave non protegge nulla.", _pwd);
            return false;
        }

        if (!Passwords.Same(first, second))
        {
            _pwd2.Text = string.Empty;
            Problem("Le due password non coincidono.", _pwd2);
            return false;
        }

        return true;
    }

    private void Problem(string message, TextBox field)
    {
        _pwdProblemText.Text = message;
        _pwdProblem.IsVisible = true;
        Kit.Shake(field);
        field.Focus();
    }

    private void OnLater()
    {
        _ws.Settings.SecurityPrompted = true;
        _ws.Settings.Save();
        _ws.Say("La nota resta in chiaro. Puoi chiuderla a chiave quando vuoi, da Impostazioni > Cassaforte.");
        _later();
    }

    private async Task OnNext()
    {
        if (_working)
        {
            return;
        }

        if (_index < _steps.Length - 1)
        {
            if (CanLeave())
            {
                Go(_index + 1);
            }

            return;
        }

        if (_understood.IsChecked != true)
        {
            Kit.Shake(_understood);
            return;
        }

        await Forge();
    }

    /// <summary>Forgia la chiave fuori dal thread della finestra e crea la cassaforte con la nota dentro.</summary>
    private async Task Forge()
    {
        _working = true;
        _next.IsEnabled = false;
        _back.IsEnabled = false;
        _forging.IsVisible = true;

        byte[] salt = VaultSession.FreshSalt();
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
        tick.Tick += (_, _) => _forgingText.Text = string.Create(CultureInfo.CurrentCulture,
            $"Forgiatura della chiave, {clock.Elapsed.TotalSeconds:N1} s. {Passwords.Cost(KdfParameters.Default)}.");
        tick.Start();

        try
        {
            // la nota com'e' nell'editor entra nella cassaforte: prima la si prende dal foglio
            await _flush();
            byte[] key;

            using (SecureString password = Secret.From(_pwd))
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

            _pwd.Text = string.Empty;
            _pwd2.Text = string.Empty;
            _understood.IsChecked = false;

            foreach (Control step in _steps)
            {
                step.IsVisible = false;
            }

            _done.IsVisible = true;
            _nav.IsVisible = false;
            Motion.Show(_done, rise: 14, ms: 360);
            Notices.Current?.Success("Cifratura attiva", "La nota vive nel contenitore: i file in chiaro sono stati rimossi.");

            DispatcherTimer back = new() { Interval = TimeSpan.FromSeconds(2.4) };
            back.Tick += (_, _) =>
            {
                back.Stop();
                _nav.IsVisible = true;
                Go(0, animate: false);
                ShowState();
            };
            back.Start();
        }
        catch (Exception problem) when (problem is CryptographicException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            // l'indicatore si ferma prima del messaggio: dietro a un errore non deve sembrare
            // che il lavoro continui
            tick.Stop();
            _forging.IsVisible = false;

            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                await Message.Tell(owner, "Cifratura non riuscita", "La nota resta com'era, in chiaro. Dettaglio: " + problem.Message);
            }
        }
        finally
        {
            tick.Stop();
            _working = false;
            _next.IsEnabled = true;
            _back.IsEnabled = true;
            _forging.IsVisible = false;
        }
    }

    // ------------------------------------------------------------------ cassaforte aperta

    private async Task OnChangePassword()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || await PasswordDialog.Ask(owner) is not { } forged)
        {
            return;
        }

        try
        {
            _ws.ChangePassword(forged.Key, forged.Kdf, forged.Salt);
            Notices.Current?.Success("Password cambiata", "La cassaforte si apre con la nuova password.");
        }
        catch (Exception problem) when (problem is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Notices.Current?.Error("Password non cambiata", "La cassaforte si apre ancora con quella di prima. Dettaglio: " + problem.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(forged.Key);
        }
    }

    private async Task OnDecrypt()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner
            || !await Message.Ask(owner, "Togliere la cifratura?", "La nota torna in chiaro su disco e il contenitore viene coperto e rimosso.", "Togli la cifratura", danger: true))
        {
            return;
        }

        await _flush();

        if (_ws.Decrypt())
        {
            Notices.Current?.Warning("Cifratura tolta", "La nota e' di nuovo in chiaro su disco.");
            ShowState();
            Go(0, animate: false);
            return;
        }

        Notices.Current?.Error("Non riuscito", "Il contenitore resta com'era: la nota e' al sicuro.");
    }

    private void OnLockMinutes()
    {
        int minutes = int.TryParse(_lockMinutes.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int typed)
            ? Math.Clamp(typed, 0, 1440)
            : _ws.Settings.AutoLockMinutes;

        _lockMinutes.Text = minutes.ToString(CultureInfo.CurrentCulture);

        if (minutes != _ws.Settings.AutoLockMinutes)
        {
            _ws.Settings.AutoLockMinutes = minutes;
            _ws.Settings.Save();
            _ws.StartTimers();
            _ws.Say(minutes == 0 ? "Blocco automatico spento." : $"Blocco automatico dopo {minutes} minuti fermi.");
        }
    }

    // ------------------------------------------------------------------ pezzi

    private static Control Step(string art, string title, string text, params Control[] body)
    {
        TextBlock heading = new() { Text = title, FontSize = 19, FontWeight = FontWeight.Bold };
        heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("Label"));
        TextBlock words = new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        words.Classes.Add("muted");

        Grid head = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(4, 0, 0, 4) };
        head.Children.Add(Art.Picture(art, 72));
        StackPanel texts = new() { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { heading, words } };
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);

        StackPanel stack = new() { Margin = new Thickness(0, 0, 6, 8), Children = { head } };
        stack.Children.AddRange(body);
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static T Spaced<T>(T control, double top, double bottom = 0) where T : Control
    {
        control.Margin = new Thickness(control.Margin.Left, top, control.Margin.Right, bottom);
        return control;
    }

    private static Border Danger(Border group)
    {
        group.Bind(Border.BorderBrushProperty, group.GetResourceObservable("RedBorder"));
        return group;
    }

    private static FontFamily Mono =>
        Application.Current?.TryFindResource("Mono", out object? family) == true && family is FontFamily mono ? mono : FontFamily.Default;
}
