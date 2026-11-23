using Avalonia.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed class DesktopHistory
{
    private readonly IPlatformServices _platform;
    private readonly DispatcherTimer _saveTimer;
    private Task _pendingSave = Task.CompletedTask;
    private IReadOnlyList<PlatformPermission>? _installingPermissions;
    private bool _signingOut;
    private string? _registeredGesture;
    private readonly HashSet<object> _recorders = [];

    public HistoryStore Store { get; }
    public bool Paused { get; private set; }
    public string? Warning { get; private set; }
    public bool ShortcutRegistrationFailed { get; private set; }
    public string Shortcut => Store.Settings.PanelShortcut ?? _platform.DefaultGesture;
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
        Store = new HistoryStore(platform.DataDirectory, platform.HistoryProtector);
        Store.Load();
        Warning = Store.LoadWarning;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

    public void Start()
    {
        _platform.Clipboard.Captured += OnCaptured;
        _platform.Clipboard.Start();
        _platform.Hotkey.Pressed += OnHotkey;

        RegisterSettingsShortcut();
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
        if (!_platform.Hotkey.TryRegister(gesture))
            return false;
        _registeredGesture = gesture;
        return true;
    }

    public void RegisterSettingsShortcut() => ShortcutRegistrationFailed =
        (_platform.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey) || _platform is DemoPlatformServices) &&
        !TryRegisterShortcut(Shortcut);

    public void ApplySettings(AppSettings settings)
    {
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

    public bool TryWrite(ClipItem item, bool plainText = false)
    {
        try
        {
            _platform.ClipboardWriter.Write(item, plainText);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
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
        Changed?.Invoke();
    }

    public void HistoryChanged()
    {
        Changed?.Invoke();
        ScheduleSave();
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

    private Task FlushSaveAsync()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            Save();
        }
        return _pendingSave;
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
        if (Paused)
            return;
        // A repeated copy updates the original entry even when Add returns false.
        if (!Store.Add(capture.Item) && Store.LastRejectionReason is not null)
            return;
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

    private void Save() => _pendingSave = SaveAsync();

    private async Task SaveAsync()
    {
        try
        {
            await Store.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Warning = Strings.Get("SaveFailed");
                Changed?.Invoke();
            });
        }
    }
}
