using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DuckNote.App.Theme;
using DuckNote.App.Vault;
using DuckNote.Core.Vault;

namespace DuckNote.App;

public partial class MainWindow
{
    private bool _locked;
    private bool _unlocking;
    private bool _veilBare;
    private double _veilDescent;
    private ScaleTransform? _veilStage;
    private VaultHeader? _lockHeader;
    private Task<byte[]>? _unlockWork;
    private readonly Stopwatch _unlockClock = new();
    private DispatcherTimer? _unlockTimer;
    private string _unlockCost = string.Empty;

    private void SetUpLockVeil()
    {
        if (VeilLoader.Children.Count > 0 && VeilLoader.Children[0] is FrameworkElement stage)
        {
            _veilStage = Motion.Scalable(stage);
        }

        ShowVeilDuck();

        LockVeil.SizeChanged += (_, _) => RecentreLoader();

        VeilGo.Click += (_, _) => StartUnlock();
        VeilPwd.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                StartUnlock();
            }
        };

        VeilClose.Click += (_, _) => Close();
        VeilMin.Click += (_, _) => WindowState = WindowState.Minimized;

        LockVeil.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    private void ShowVeilDuck()
    {
        if (Assets.DuckImage.Bitmap is { } duck)
        {
            VeilImg.Source = duck;
            VeilImg.Visibility = Visibility.Visible;
            VeilVec.Visibility = Visibility.Collapsed;
            return;
        }

        VeilImg.Visibility = Visibility.Collapsed;
        VeilVec.Visibility = Visibility.Visible;
    }

    private void ShowLockScreen()
    {
        _locked = true;
        _unlocking = false;
        _veilBare = false;

        _unlockWork = null;
        _lockHeader = null;
        VeilPwd.Clear();
        VeilMsg.Visibility = Visibility.Collapsed;
        VeilWork.Visibility = Visibility.Collapsed;
        VeilForm.IsEnabled = true;
        VeilCard.Opacity = 1;

        VeilBody.BeginAnimation(OpacityProperty, null);
        VeilBodyT.BeginAnimation(TranslateTransform.YProperty, null);
        VeilLoaderT.BeginAnimation(TranslateTransform.YProperty, null);
        VeilBody.Opacity = 1;
        VeilBodyT.Y = 0;
        VeilLoaderT.Y = 0;

        VeilSeal.Opacity = 0;
        VeilRing1.Opacity = 0.5;
        VeilRing2.Opacity = 0.45;
        Stroke(VeilRing1, "Accent");
        Stroke(VeilRing2, "Blue");

        AppGrid.Visibility = Visibility.Hidden;

        LockVeil.Visibility = Visibility.Visible;
        LockVeil.BeginAnimation(OpacityProperty, Motion.Slide(0, 1, 220, Motion.Ease()));

        UpdateLockButton();
        VeilPwd.Focus();
    }

    private void StartUnlock()
    {
        if (_unlocking || !_locked)
        {
            return;
        }

        using SecureString password = VeilPwd.SecurePassword;

        if (password.Length == 0)
        {
            ShowVeilError("Serve la password.");
            return;
        }

        _lockHeader = VaultSession.Header();
        if (_lockHeader is null)
        {
            ShowVeilError("Il contenitore non si legge: store.bin non e' una cassaforte di DuckNote.");
            return;
        }

        _unlocking = true;
        _unlockCost = $"Argon2id {_lockHeader.Kdf.MemoryKib / 1024} MiB, {_lockHeader.Kdf.Passes} passate, " +
                      $"poi PBKDF2-SHA512 con {_lockHeader.Kdf.Pbkdf2Iterations:N0} giri.";

        StartVeilWork("Apertura della cassaforte. " + _unlockCost);
        _unlockClock.Restart();

        _unlockWork = VaultSession.DeriveAsync(password, _lockHeader.Salt, _lockHeader.Kdf);

        _unlockTimer?.Stop();
        _unlockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _unlockTimer.Tick += (_, _) => StepUnlock();
        _unlockTimer.Start();
    }

    private void StepUnlock()
    {
        VeilWork.Text = $"Apertura della cassaforte, {_unlockClock.Elapsed.TotalSeconds:N1} s. {_unlockCost}";

        if (_unlockWork is not { IsCompleted: true })
        {
            return;
        }

        _unlockTimer?.Stop();
        _unlockClock.Stop();

        if (_unlockWork.IsFaulted)
        {
            ShowVeilError("Derivazione non riuscita: " +
                (_unlockWork.Exception?.GetBaseException().Message ?? "errore"));
            return;
        }

        byte[] key = _unlockWork.Result;
        bool opened = _vault.Unlock(key);
        CryptographicOperations.ZeroMemory(key);

        if (!opened)
        {
            DenyVeil();
            return;
        }

        ShowVeilSuccess();
    }

    private void ShowVeilSuccess()
    {
        _unlocking = false;
        VeilWork.Text = "Apertura della nota...";
        VeilWork.Visibility = Visibility.Visible;
        VeilForm.IsEnabled = false;
        VeilRing1.Opacity = 0;
        VeilRing2.Opacity = 0;
        VeilSeal.Opacity = 1;

        IEasingFunction spring = Motion.Ease("Back", EasingMode.EaseOut, 0.5);
        VeilSealS.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.Slide(0.6, 1, 420, spring));
        VeilSealS.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Slide(0.6, 1, 420, spring));

        DoubleAnimation hop = Motion.Slide(0, -9, 190, Motion.Ease());
        hop.AutoReverse = true;
        VeilDuckT.BeginAnimation(TranslateTransform.YProperty, hop);

        DispatcherTimer farewell = new() { Interval = TimeSpan.FromMilliseconds(520) };
        farewell.Tick += (_, _) =>
        {
            farewell.Stop();
            CompleteUnlock();
        };
        farewell.Start();
    }

    private void CompleteUnlock()
    {
        AppGrid.Visibility = Visibility.Visible;
        LoadNote();
        VaultSession.RemoveLeftovers();
        RecolourHosts();
        RefreshSideList();

        DoubleAnimation fade = Motion.Slide(1, 0, 260, Motion.Ease(mode: EasingMode.EaseIn));
        fade.Completed += (_, _) =>
        {
            LockVeil.Visibility = Visibility.Collapsed;
            LockVeil.BeginAnimation(OpacityProperty, null);
        };
        LockVeil.BeginAnimation(OpacityProperty, fade);

        _locked = false;
        _lastActivity = DateTime.UtcNow;
        ApplyTimers();
        UpdateLockButton();
        StatusText.Text = $"Riaperta alle {DateTime.Now:HH:mm}.";
        Editor.Focus();
    }

    private void DenyVeil()
    {
        StopVeilWork();
        ShowVeilForm();
        VeilMsg.Visibility = Visibility.Collapsed;
        VeilWork.Visibility = Visibility.Collapsed;

        if (Denial.AlarmOf(VeilPwd) is null)
        {
            Motion.Shake(VeilShake);
            VeilPwd.Clear();
            VeilPwd.Focus();
            return;
        }

        Denial.Play(
            new Denial.Stage([VeilPwd], [VeilPwdLabel],
                [VeilRing1, VeilRing2], [VeilRot1, VeilRot2], VeilShake),
            _theme);
    }

    private void ShowVeilError(string message)
    {
        StopVeilWork();

        ShowVeilForm();

        Stroke(VeilRing1, "Red");
        Stroke(VeilRing2, "Red");
        VeilMsg.Text = message;
        VeilMsg.Visibility = Visibility.Visible;
        VeilWork.Visibility = Visibility.Collapsed;

        Motion.Shake(VeilShake);
        VeilPwd.Clear();
        VeilPwd.Focus();
    }

    private void StartVeilWork(string message)
    {
        VeilForm.IsEnabled = false;
        VeilGo.IsEnabled = false;
        VeilMsg.Visibility = Visibility.Collapsed;
        VeilWork.Text = message;
        VeilWork.Visibility = Visibility.Visible;

        HideVeilForm();

        VeilRing1.Opacity = 0.95;
        VeilRing2.Opacity = 0.85;
        Stroke(VeilRing1, "Accent");
        Stroke(VeilRing2, "Blue");

        VeilRot1.BeginAnimation(RotateTransform.AngleProperty, Motion.Spin(1900));
        VeilRot2.BeginAnimation(RotateTransform.AngleProperty, Motion.Spin(2700, 360, 0));

        Motion.Grow(_veilStage, Motion.WorkingScale, 420);

        VeilDuckS.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.Pulse(1, 1.07, 780));
        VeilDuckS.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Pulse(1, 1.07, 780));
    }

    private void StopVeilWork()
    {
        _unlocking = false;
        _unlockTimer?.Stop();

        VeilRot1.BeginAnimation(RotateTransform.AngleProperty, null);
        VeilRot2.BeginAnimation(RotateTransform.AngleProperty, null);
        VeilDuckS.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        VeilDuckS.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Motion.Grow(_veilStage, 1, 380);

        VeilForm.IsEnabled = true;
        VeilGo.IsEnabled = true;
        VeilRing1.Opacity = 0.5;
        VeilRing2.Opacity = 0.45;
    }

    private void HideVeilForm()
    {
        if (_veilBare)
        {
            return;
        }
        _veilBare = true;
        _veilDescent = VeilDescent();

        VeilBody.IsHitTestVisible = false;

        DoubleAnimation leave = Motion.Slide(1, 0, 200, Motion.Ease(mode: EasingMode.EaseIn));
        leave.Completed += (_, _) => VeilPwd.Clear();
        VeilBody.BeginAnimation(OpacityProperty, leave);
        VeilBodyT.BeginAnimation(TranslateTransform.YProperty,
            Motion.Slide(0, 18, 240, Motion.Ease(mode: EasingMode.EaseIn)));
        VeilLoaderT.BeginAnimation(TranslateTransform.YProperty,
            Motion.Slide(0, _veilDescent, 520, Motion.Ease("Cubic", EasingMode.EaseInOut)));
    }

    private double VeilDescent()
    {
        LockVeil.UpdateLayout();

        FrameworkElement stage = VeilLoader.Children.Count > 0 && VeilLoader.Children[0] is FrameworkElement first
            ? first
            : VeilLoader;

        double centre = LockVeil.ActualHeight / 2;
        double top = stage.TranslatePoint(new Point(0, 0), LockVeil).Y;
        return Math.Round(centre - (top + (stage.ActualHeight / 2)));
    }

    private void RecentreLoader()
    {
        if (!_veilBare || LockVeil.Visibility != Visibility.Visible)
        {
            return;
        }

        _veilDescent += VeilDescent();

        VeilLoaderT.BeginAnimation(TranslateTransform.YProperty, null);
        VeilLoaderT.Y = _veilDescent;
    }

    private void ShowVeilForm()
    {
        if (!_veilBare)
        {
            return;
        }
        _veilBare = false;

        IEasingFunction ease = Motion.Ease();
        VeilBody.IsHitTestVisible = true;
        VeilBody.BeginAnimation(OpacityProperty, Motion.Slide(0, 1, 300, ease));
        VeilBodyT.BeginAnimation(TranslateTransform.YProperty, Motion.Slide(18, 0, 320, ease));
        VeilLoaderT.BeginAnimation(TranslateTransform.YProperty,
            Motion.Slide(_veilDescent, 0, 460, Motion.Ease("Cubic", EasingMode.EaseInOut)));
        VeilWork.Visibility = Visibility.Collapsed;
    }

    private void Stroke(System.Windows.Shapes.Shape shape, string token) =>
        shape.Stroke = ThemeTokens.Brush(ThemeTokens.Colour(_theme, token));
}
