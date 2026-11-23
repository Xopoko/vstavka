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
    private string? _registeredGesture;
    private readonly HashSet<object> _recorders = [];

    public HistoryStore Store { get; }
    public bool Paused { get; private set; }
    public string? Warning { get; private set; }
    public bool ShortcutRegistrationFailed { get; private set; }
    public string Shortcut => Store.Settings.PanelShortcut ?? _platform.DefaultGesture;
    public event Action? Changed;
    public event Action? SettingsChanged;
    public event Action? ToggleRequested;

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

    public void TogglePause()
    {
        Paused = !Paused;
        Changed?.Invoke();
    }

    public void HistoryChanged()
    {
        Changed?.Invoke();
        if (!Store.IsPersistent || Store.SavingBlocked)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Stop()
    {
        _platform.Clipboard.Captured -= OnCaptured;
        _platform.Hotkey.Pressed -= OnHotkey;
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            Save();
        }
        _pendingSave.GetAwaiter().GetResult();
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
