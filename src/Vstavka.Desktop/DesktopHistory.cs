using Avalonia.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;
using Vstavka.Platform.MacOS;
using Vstavka.Platform.Windows;

namespace Vstavka.Desktop;

internal sealed class DesktopHistory
{
    private readonly IPlatformServices _platform;
    private readonly WindowsStoreProfile? _windowsStoreProfile;
    private readonly DispatcherTimer _saveTimer;
    private Task<bool> _pendingSave = Task.FromResult(true);
    private int _saveSequence;
    private IReadOnlyList<PlatformPermission>? _installingPermissions;
    private bool _signingOut;
    private bool _monitoringClipboard;
    private DateTimeOffset _discardCapturesThrough;
    private string? _registeredGesture;
    private readonly HashSet<object> _recorders = [];

    public HistoryStore Store { get; }
    public bool Paused { get; private set; }
    public string? Warning { get; private set; }
    public PlatformNotice? CaptureNotice { get; private set; }
    public bool ShortcutRegistrationFailed { get; private set; }
    public bool IsStoreBuild => _windowsStoreProfile is not null || _platform is MacPlatformServices { IsAppStoreBuild: true };
    public bool ClipboardConsentRequired => _windowsStoreProfile is { HasClipboardConsent: false };
    public bool NeedsShortcut => _platform.RequiresExplicitShortcut && Store.Settings.PanelShortcut is null;
    public string Shortcut => Store.Settings.PanelShortcut ?? (NeedsShortcut ? "" : _platform.DefaultGesture);
    public IReadOnlyList<PlatformPermission> Permissions => _installingPermissions ?? _platform.Permissions;
    public bool InstallingGnomeHelper => _installingPermissions is not null;
    public bool GnomeHelperInstallFailed { get; private set; }
    public bool AccessibilityRequested { get; set; }
    public event Action? Changed;
    public event Action? SettingsChanged;
    public event Action? ToggleRequested;
    public event Action? GnomeHelperInstallChanged;

    public DesktopHistory(IPlatformServices platform)
    {
        _platform = platform;
        _windowsStoreProfile = (platform as WindowsPlatformServices)?.StoreProfile;
        Store = new HistoryStore(platform.DataDirectory, platform.HistoryProtector);
        Store.Load();
        _windowsStoreProfile?.ApplyLegacyPreferences(Store.Settings);
        Warning = Store.LoadWarning;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) => QueueSave();
    }

    public void Start()
    {
        _platform.Hotkey.Pressed += OnHotkey;
        if (!ClipboardConsentRequired)
            StartClipboardMonitor();
        RegisterSettingsShortcut();
    }

    public bool TryEnableClipboardHistory()
    {
        if (_windowsStoreProfile is null || !_windowsStoreProfile.TryGrantClipboardConsent())
            return false;
        StartClipboardMonitor();
        return true;
    }

    private void StartClipboardMonitor()
    {
        if (_monitoringClipboard)
            return;
        _platform.Clipboard.Captured += OnCaptured;
        var windows = _platform as WindowsPlatformServices;
        if (windows is not null)
            windows.ClipboardNotice += OnCaptureNotice;
        try
        {
            _platform.Clipboard.Start();
            _monitoringClipboard = true;
        }
        catch
        {
            _platform.Clipboard.Captured -= OnCaptured;
            if (windows is not null)
                windows.ClipboardNotice -= OnCaptureNotice;
            throw;
        }
    }

    public void SetRecording(object recorder, bool recording)
    {
        if (recording)
            _recorders.Add(recorder);
        else
            _recorders.Remove(recorder);
    }

    public bool TryRegisterShortcut(string gesture)
    {
        if (_registeredGesture == gesture)
            return true;
        if (_platform.RequiresExplicitShortcut && !_platform.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey))
            return true;
        if (!_platform.Hotkey.TryRegister(gesture))
            return false;
        _registeredGesture = gesture;
        return true;
    }

    public void RegisterSettingsShortcut() => ShortcutRegistrationFailed =
        !NeedsShortcut && (_platform.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey) || _platform is DemoPlatformServices) &&
        !TryRegisterShortcut(Shortcut);

    internal void RestoreSettingsShortcut()
    {
        _registeredGesture = null;
        RegisterSettingsShortcut();
    }

    public void ApplySettings(AppSettings settings)
    {
        settings.StoreReview = Store.Settings.StoreReview;
        Store.Settings = settings;
        Store.ApplyRetention();
        RegisterSettingsShortcut();
        SettingsChanged?.Invoke();
        HistoryChanged();
    }

    public void SetOnboardingStep(int step)
    {
        Store.Settings.OnboardingStep = step;
        ScheduleSave();
    }

    public string? WriteToClipboard(ClipItem item, bool plainText = false)
    {
        try
        {
            _platform.ClipboardWriter.Write(item, plainText);
            return null;
        }
        catch (FileNotFoundException) when (item.Kind == ClipKind.Files && !plainText)
        {
            return Strings.Get("FileMissing");
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return Strings.Get("ClipboardWriteFailed");
        }
    }

    public void CountStoreReviewUse()
    {
        if (_platform.StoreReview is null)
            return;
        var state = Store.Settings.StoreReview;
        var updated = state.CountUse(DateTimeOffset.UtcNow);
        if (updated == state)
            return;
        Store.Settings.StoreReview = updated;
        ScheduleSave();
    }

    public void TryRequestStoreReview()
    {
        if (_platform.StoreReview is not { } review || !Store.IsPersistent || Store.SavingBlocked)
            return;
        var state = Store.Settings.StoreReview;
        if (review.AppVersion is not { } version || !state.ShouldRequest(version, DateTimeOffset.UtcNow) || !review.TryRequest())
            return;
        Store.Settings.StoreReview = state.MarkRequested(version);
        QueueSave();
    }

    public async Task InstallGnomeHelperAsync(Action install)
    {
        if (InstallingGnomeHelper)
            return;

        _installingPermissions = _platform.Permissions;
        GnomeHelperInstallFailed = false;
        try
        {
            GnomeHelperInstallChanged?.Invoke();
            try
            {
                await Task.Run(install);
            }
            catch (Exception)
            {
                GnomeHelperInstallFailed = true;
            }
        }
        finally
        {
            _installingPermissions = null;
            GnomeHelperInstallChanged?.Invoke();
        }
    }

    public void TogglePause()
    {
        Paused = !Paused;
        if (!Paused)
            _discardCapturesThrough = DateTimeOffset.UtcNow;
        CaptureNotice = null;
        Changed?.Invoke();
    }

    public void HistoryChanged()
    {
        Changed?.Invoke();
        ScheduleSave();
    }

    public Task<bool> ClearHistoryAsync()
    {
        if (Store.SavingBlocked)
            return Task.FromResult(false);
        _discardCapturesThrough = DateTimeOffset.UtcNow;
        Store.Clear(includePinned: true);
        CaptureNotice = null;
        Changed?.Invoke();
        return QueueSave();
    }

    private void ScheduleSave()
    {
        if (!Store.IsPersistent || Store.SavingBlocked)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Stop()
    {
        _platform.Clipboard.Captured -= OnCaptured;
        if (_platform is WindowsPlatformServices windows)
            windows.ClipboardNotice -= OnCaptureNotice;
        _platform.Hotkey.Pressed -= OnHotkey;
        FlushSaveAsync().GetAwaiter().GetResult();
    }

    public async Task SignOutAsync(Action signOut)
    {
        if (_signingOut)
            return;
        _signingOut = true;
        try
        {
            await FlushSaveAsync();
            signOut();
        }
        finally { _signingOut = false; }
    }

    private Task FlushSaveAsync() => _saveTimer.IsEnabled ? QueueSave() : _pendingSave;

    private void OnCaptureNotice(PlatformNotice notice)
    {
        if (Paused)
            return;
        CaptureNotice = notice;
        Changed?.Invoke();
    }

    private void OnCaptured(ClipboardCapture capture)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Add(capture);
        else
            Dispatcher.UIThread.Post(() => Add(capture));
    }

    private void Add(ClipboardCapture capture)
    {
        if (Paused || capture.Item.CapturedAt <= _discardCapturesThrough)
            return;
        // A repeated copy updates the original entry even when Add returns false.
        if (!Store.Add(capture.Item) && Store.LastRejectionReason is not null)
            return;
        CaptureNotice = null;
        if (capture.Source is { IconPng: { } png } source)
            Store.SetSourceIcon(source.Name, png);
        HistoryChanged();
    }

    private void OnHotkey()
    {
        if (Dispatcher.UIThread.CheckAccess())
            ToggleFromHotkey();
        else
            Dispatcher.UIThread.Post(ToggleFromHotkey);
    }

    private void ToggleFromHotkey()
    {
        if (_recorders.Count == 0)
            ToggleRequested?.Invoke();
    }

    private Task<bool> QueueSave()
    {
        _saveTimer.Stop();
        return _pendingSave = SaveAsync(++_saveSequence);
    }

    private async Task<bool> SaveAsync(int sequence)
    {
        bool saved;
        try
        {
            await Store.SaveAsync().ConfigureAwait(false);
            saved = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or System.Security.Cryptography.CryptographicException)
        {
            saved = false;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var warning = saved ? Store.LoadWarning : Strings.Get("SaveFailed");
            if (sequence != _saveSequence || Warning == warning)
                return;
            Warning = warning;
            Changed?.Invoke();
        });
        return saved;
    }
}
