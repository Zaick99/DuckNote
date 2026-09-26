using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DuckNote.App.Theme;
using DuckNote.App.Vault;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;

namespace DuckNote.App.Views;

public enum GateMode
{
    Create,

    Unlock,

    Change
}

public sealed record GateOutcome(bool Accepted, byte[]? Key, byte[]? Salt, KdfParameters? Kdf, bool Abandoned);

public partial class GateWindow : Window
{
    private readonly GateMode _mode;
    private readonly KdfParameters _kdf;
    private readonly byte[] _salt;

    private readonly Stopwatch _clock = new();
    private System.Windows.Threading.DispatcherTimer? _tick;
    private Task<byte[]>? _derivation;
    private bool _bare;
    private double _descent;
    private double _fullHeight;
    private ScaleTransform? _stage;

    private string _theme = "light";

    public GateOutcome Outcome { get; private set; } = new(false, null, null, null, Abandoned: false);

    public Func<byte[], bool>? Verify { get; set; }

    public GateWindow(GateMode mode, KdfParameters kdf, byte[]? salt, string theme)
    {
        InitializeComponent();

        _mode = mode;
        _kdf = kdf;
        _salt = salt ?? RandomNumberGenerator.GetBytes(32);
        _theme = theme;

        ThemeTokens.Apply(Resources, theme);
        _stage = Motion.Scalable(GStage);
        ShowDuck();
        Apply(mode);

        GGo.Click += (_, _) => Begin();
        GLater.Click += (_, _) => Leave(abandoned: false);
        GClose.Click += (_, _) => Leave(abandoned: true);

        GPwd.PasswordChanged += (_, _) => UpdateStrength();
        GPwd.KeyDown += OnFieldKey;
        GPwd2.KeyDown += OnFieldKey;

        ContentRendered += (_, _) => Motion.OpenDialog(GRoot);

        GTitleBar.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };

        Loaded += (_, _) =>
        {
            if (Owner is null)
            {
                Topmost = true;
                Activate();
            }

            GPwd.Focus();
        };
    }

    private void OnFieldKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Begin();
        }
        else if (e.Key == Key.Escape)
        {
            Leave(abandoned: true);
        }
    }

    private void ShowDuck()
    {
        if (Assets.DuckImage.Bitmap is { } duck)
        {
            GDuckImg.Source = duck;
            GDuckImg.Visibility = Visibility.Visible;
            GDuckVec.Visibility = Visibility.Collapsed;
            return;
        }

        GDuckImg.Visibility = Visibility.Collapsed;
        GDuckVec.Visibility = Visibility.Visible;
    }

    private void Apply(GateMode mode)
    {
        bool creating = mode != GateMode.Unlock;

        GTitle.Text = mode switch
        {
            GateMode.Create => "Chiudi la nota a chiave",
            GateMode.Change => "Cambia la password",
            _ => "La nota e' chiusa a chiave"
        };

        GSub.Text = mode switch
        {
            GateMode.Create =>
                "La password non diventa la chiave: ne sblocca una casuale, con cui la nota e' cifrata. " +
                "Se la perdi, la nota e' persa: non c'e' recupero.",
            GateMode.Change =>
                "Cambia solo l'involucro della chiave: il contenuto resta dov'e'.",
            _ => "Serve la password per aprirla."
        };

        GLbl2.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        GPwd2.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        GStrength.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        GLater.Visibility = mode == GateMode.Create ? Visibility.Visible : Visibility.Collapsed;

        GGo.Content = mode == GateMode.Unlock ? "Apri" : "Chiudi a chiave";
        GMsg.Visibility = Visibility.Collapsed;
        GWork.Visibility = Visibility.Collapsed;
    }

    private string Cost => $"Argon2id {_kdf.MemoryKib / 1024} MiB, {_kdf.Passes} passate.";

    private void UpdateStrength()
    {
        if (GStrength.Visibility != Visibility.Visible)
        {
            return;
        }

        int bits;
        using (SecureString typed = GPwd.SecurePassword)
        {
            bits = PasswordStrength.Bits(typed);
        }

        (string token, string word) = PasswordStrength.Describe(bits);

        GBar.Background = ThemeTokens.Brush(ThemeTokens.Colour(_theme, token));

        double full = Math.Max(60.0, ((FrameworkElement)GBar.Parent).ActualWidth);
        GBar.BeginAnimation(WidthProperty,
            Motion.Slide(GBar.ActualWidth, full * Math.Min(1.0, bits / 110.0), 260, Motion.Ease()));

        GBarTxt.Text = bits == 0 ? string.Empty : $"{bits} bit, {word}";
    }

    private void Begin()
    {
        if (!GGo.IsEnabled)
        {
            return;
        }

        using SecureString password = GPwd.SecurePassword;
        using SecureString repeated = GPwd2.SecurePassword;

        if (password.Length == 0)
        {
            Fail("Serve una password.");
            return;
        }

        if (_mode != GateMode.Unlock)
        {
            if (password.Length < 8)
            {
                Fail("Almeno otto caratteri: sotto questa soglia la chiave non protegge nulla.");
                return;
            }

            if (!PasswordStrength.Same(password, repeated))
            {
                Deny(bothFields: true);
                return;
            }
        }

        Working(true);
        _clock.Restart();
        _derivation = VaultSession.DeriveAsync(password, _salt, _kdf);

        _tick = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _tick.Tick += (_, _) => Step();
        _tick.Start();
    }

    private void Step()
    {
        GWork.Text = $"Forgiatura della chiave, {_clock.Elapsed.TotalSeconds:N1} s. {Cost}";

        if (_derivation is not { IsCompleted: true })
        {
            return;
        }

        _tick?.Stop();
        _clock.Stop();

        if (_derivation.IsFaulted)
        {
            Fail("Derivazione non riuscita: " + (_derivation.Exception?.GetBaseException().Message ?? "errore"));
            return;
        }

        byte[] key = _derivation.Result;

        if (Verify is { } opens && !opens(key))
        {
            Deny();
            return;
        }

        Outcome = new GateOutcome(Accepted: true, key, _salt, _kdf, Abandoned: false);
        DialogResult = true;
    }

    private void Deny(bool bothFields = false)
    {
        Working(false);
        GMsg.Visibility = Visibility.Collapsed;

        if (Denial.AlarmOf(GPwd) is null)
        {
            Motion.Shake(GShake);
            GPwd.Clear();
            GPwd2.Clear();
            GPwd.Focus();
            return;
        }

        PasswordBox[] fields = bothFields ? [GPwd, GPwd2] : [GPwd];
        TextBlock[] labels = bothFields ? [GLbl1, GLbl2] : [GLbl1];

        Denial.Play(
            new Denial.Stage(fields, labels, [GRing1, GRing2], [GRot1, GRot2], GShake),
            _theme);
    }

    private void Working(bool busy)
    {
        GForm.IsEnabled = !busy;
        GGo.IsEnabled = !busy;
        GLater.IsEnabled = !busy;
        GMsg.Visibility = Visibility.Collapsed;
        GWork.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (!busy)
        {
            StopWork();
            ShowForm();
            return;
        }

        GWork.Text = "Forgiatura della chiave. " + Cost;
        HideForm();

        GRing1.Opacity = 0.95;
        GRing2.Opacity = 0.85;
        GRot1.BeginAnimation(RotateTransform.AngleProperty, Motion.Spin(1900));
        GRot2.BeginAnimation(RotateTransform.AngleProperty, Motion.Spin(2700, 360, 0));

        Motion.Grow(_stage, Motion.WorkingScale, 420);

        GDuckS.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.Pulse(1, 1.07, 780));
        GDuckS.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Pulse(1, 1.07, 780));
    }

    private void StopWork()
    {
        GRot1.BeginAnimation(RotateTransform.AngleProperty, null);
        GRot2.BeginAnimation(RotateTransform.AngleProperty, null);
        GDuckS.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        GDuckS.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Motion.Grow(_stage, 1, 380);
        GRing1.Opacity = 0.5;
        GRing2.Opacity = 0.45;
    }

    private const double CardMargin = 30;
    private const double StageAir = 16;

    private (double Height, double Descent) CompactPlan()
    {
        UpdateLayout();

        double height = Math.Round(ActualWidth);

        height = Math.Min(height, Math.Round(ActualHeight));

        double restingTop = GStage.TranslatePoint(new Point(0, 0), GRoot).Y;
        double wanted = height / 2;

        return (height, wanted - (restingTop + (GStage.ActualHeight / 2)));
    }

    private void HideForm()
    {
        if (_bare)
        {
            return;
        }
        _bare = true;

        _fullHeight = Math.Round(ActualHeight);
        (double compact, _descent) = CompactPlan();

        IEasingFunction easeIn = Motion.Ease(mode: EasingMode.EaseIn);
        GBody.IsHitTestVisible = false;
        GFooter.IsHitTestVisible = false;

        DoubleAnimation leave = Motion.Slide(1, 0, 180, easeIn);
        leave.Completed += (_, _) =>
        {
            GPwd.Clear();
            GPwd2.Clear();
        };

        GBody.BeginAnimation(OpacityProperty, leave);
        GFooter.BeginAnimation(OpacityProperty, Motion.Slide(1, 0, 140, easeIn));
        GBodyT.BeginAnimation(TranslateTransform.YProperty, Motion.Slide(0, 18, 240, easeIn));
        GLoaderT.BeginAnimation(TranslateTransform.YProperty,
            Motion.Slide(0, _descent, 560, Motion.Ease("Cubic", EasingMode.EaseInOut)));

        Resize(compact, 560);
    }

    private void ShowForm()
    {
        if (!_bare)
        {
            return;
        }
        _bare = false;
        GWork.Visibility = Visibility.Collapsed;

        IEasingFunction ease = Motion.Ease();
        GBody.IsHitTestVisible = true;
        GFooter.IsHitTestVisible = true;
        GBody.BeginAnimation(OpacityProperty, Motion.Slide(0, 1, 300, ease));
        GFooter.BeginAnimation(OpacityProperty, Motion.Slide(0, 1, 300, ease));
        GBodyT.BeginAnimation(TranslateTransform.YProperty, Motion.Slide(18, 0, 320, ease));
        GLoaderT.BeginAnimation(TranslateTransform.YProperty,
            Motion.Slide(_descent, 0, 500, Motion.Ease("Cubic", EasingMode.EaseInOut)));

        if (_fullHeight <= 0)
        {
            return;
        }

        Resize(_fullHeight, 500);

        System.Windows.Threading.DispatcherTimer settle = new()
        {
            Interval = TimeSpan.FromMilliseconds(540)
        };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (_bare)
            {
                return;
            }

            BeginAnimation(HeightProperty, null);
            BeginAnimation(TopProperty, null);
            Height = _fullHeight;
            SizeToContent = SizeToContent.Height;
        };
        settle.Start();
    }

    private void Resize(double height, int milliseconds)
    {
        double from = Math.Round(ActualHeight);
        if (Math.Abs(from - height) < 2)
        {
            return;
        }

        IEasingFunction soft = Motion.Ease("Cubic", EasingMode.EaseInOut);

        SizeToContent = SizeToContent.Manual;
        Height = from;
        BeginAnimation(HeightProperty, Motion.Slide(from, height, milliseconds, soft));

        if (!double.IsNaN(Top))
        {
            BeginAnimation(TopProperty, Motion.Slide(Top, Top + ((from - height) / 2), milliseconds, soft));
        }
    }

    public void Fail(string message)
    {
        Working(false);
        GMsg.Text = message;
        GMsg.Visibility = Visibility.Visible;
        GPwd.Clear();
        GPwd2.Clear();
        GPwd.Focus();

        Motion.Shake(GShake);
    }

    private void Leave(bool abandoned)
    {
        Outcome = new GateOutcome(false, null, null, null, Abandoned: abandoned);
        DialogResult = false;
    }
}
