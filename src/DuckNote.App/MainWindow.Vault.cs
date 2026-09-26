using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DuckNote.App.Editor;
using DuckNote.App.Storage;
using DuckNote.App.Theme;
using DuckNote.App.Vault;
using DuckNote.App.Views;
using DuckNote.Core.Crypto;
using DuckNote.Core.Vault;

namespace DuckNote.App;

public partial class MainWindow
{
    private readonly VaultSession _vault;
    private DispatcherTimer? _idleTimer;
    private DateTime _lastActivity = DateTime.UtcNow;

    private const string ShackleClosed = "M3,7 V4.6 A4,4 0 0 1 11,4.6 V7";
    private const string ShackleOpen = "M3,7 V4.6 A4,4 0 0 1 11,4.6 V5.4";

    private void SetUpVault()
    {
        BtnLock.Click += (_, _) =>
        {
            if (_vault.IsOpen)
            {
                LockVault("a mano");
                return;
            }

            OfferEncryption();
        };

        BtnPrefs.Click += (_, _) => ShowPreferences();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.L || Keyboard.Modifiers != ModifierKeys.Control || !_vault.IsOpen || _locked)
            {
                return;
            }

            LockVault("a mano");
            e.Handled = true;
        };

        UpdateLockButton();
        StartIdleWatch();
        SetUpLockVeil();
    }

    private void UpdateLockButton()
    {
        BtnLock.Visibility = _locked ? Visibility.Collapsed : Visibility.Visible;
        BtnLock.ToolTip = _vault.IsOpen
            ? "Blocca adesso (Ctrl+L)"
            : "Chiudi la nota a chiave";

        try
        {
            LockShackle.Data = Geometry.Parse(_vault.IsOpen ? ShackleClosed : ShackleOpen);
        }
        catch (FormatException)
        {
        }
    }

    private void ShowPreferences()
    {
        PreferencesWindow window = new(_settings, _theme, VaultSession.Exists, _vault.IsOpen, Resources)
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        _theme = _settings.FollowSystemTheme ? ThemeTokens.FromSystem() : _settings.Theme;
        ApplyTheme(_theme);
        _renderer.LiveFormatting = _settings.LiveFormatting;
        StartMonitor();
        StartIdleWatch();
        RebuildDucks();
        RefreshSideList();
        _formatter.FormatAll();

        switch (window.Request)
        {
            case SecurityRequest.Enable:
                OfferEncryption();
                break;
            case SecurityRequest.ChangePassword:
                ChangePassword();
                break;
            case SecurityRequest.Disable:
                DisableEncryption();
                break;
            case SecurityRequest.Wipe:
                WipeEverything();
                break;
            default:
                break;
        }
    }

    private void ChangePassword()
    {
        GateWindow gate = new(GateMode.Change, KdfParameters.Default, null, _theme) { Owner = this };
        if (gate.ShowDialog() != true || gate.Outcome.Key is not { } key)
        {
            return;
        }

        try
        {
            _vault.ChangePassword(key, gate.Outcome.Kdf!, gate.Outcome.Salt!);
            StatusText.Text = "Password cambiata.";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private void DisableEncryption()
    {
        if (MessageBox.Show(this,
                "La nota torna in chiaro su disco, e il contenitore viene rimosso. Procedere?",
                "Togliere la cifratura", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK)
        {
            return;
        }

        if (_vault.Disable(NoteDocument.ToXaml(Editor)))
        {
            UpdateLockButton();
            StatusText.Text = "Cifratura tolta: la nota e' di nuovo in chiaro.";
            return;
        }

        StatusText.Text = "Non riuscito: il contenitore resta, la nota e' al sicuro.";
    }

    private void WipeEverything()
    {
        _saveTimer?.Stop();
        _formatTimer?.Stop();
        _monitorTimer?.Stop();
        _idleTimer?.Stop();
        _noteDirty = false;

        _vault.Dispose();
        AppWipe.Remove();
        Application.Current.Shutdown();
    }

    private void OfferEncryption()
    {
        GateWindow gate = new(GateMode.Create, KdfParameters.Default, null, _theme) { Owner = this };
        if (gate.ShowDialog() != true || gate.Outcome.Key is not { } key)
        {
            StatusText.Text = "La nota resta in chiaro. Puoi chiuderla a chiave quando vuoi.";
            return;
        }

        try
        {
            _vault.Create(key, gate.Outcome.Kdf!, gate.Outcome.Salt!, NoteDocument.ToXaml(Editor));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _noteDirty = false;
        UpdateLockButton();
        StatusText.Text = "Nota chiusa a chiave. Da ora nulla viene scritto in chiaro.";
    }

    private void LockVault(string reason)
    {
        if (!_vault.IsOpen)
        {
            return;
        }

        SaveNote();
        _saveTimer?.Stop();
        _formatTimer?.Stop();

        _detail?.Dispose();
        UpdateInspectGlyph();

        _vault.Lock();
        Editor.Document = NoteDocument.Empty(_editorBrushes.Text);

        UpdateLockButton();
        StatusText.Text = $"Chiusa a chiave ({reason}).";

        ShowLockScreen();
    }

    private void ApplyTimers()
    {
        _saveTimer?.Start();
        _formatTimer?.Start();
    }

    private void StartIdleWatch()
    {
        PreviewKeyDown += (_, _) => _lastActivity = DateTime.UtcNow;
        PreviewMouseMove += (_, _) => _lastActivity = DateTime.UtcNow;
        PreviewMouseDown += (_, _) => _lastActivity = DateTime.UtcNow;

        _idleTimer?.Stop();
        if (_settings.AutoLockMinutes <= 0)
        {
            return;
        }

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _idleTimer.Tick += (_, _) =>
        {
            if (!_vault.IsOpen || _settings.AutoLockMinutes <= 0)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastActivity).TotalMinutes >= _settings.AutoLockMinutes)
            {
                LockVault($"ferma da {_settings.AutoLockMinutes} minuti");
            }
        };
        _idleTimer.Start();
    }
}
