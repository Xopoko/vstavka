using Avalonia.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed class DesktopHistory
{
    private readonly IPlatformServices _platform;
    private readonly bool _persistent;
    private readonly DispatcherTimer _saveTimer;
    private Task _pendingSave = Task.CompletedTask;

    public HistoryStore Store { get; }
    public bool Paused { get; private set; }
    public string? Warning { get; private set; }
    public event Action? Changed;
    public event Action? ToggleRequested;

    public DesktopHistory(IPlatformServices platform)
    {
        _platform = platform;
        var protector = platform.HistoryProtector;
        _persistent = protector is not null;
        Store = new HistoryStore(platform.DataDirectory, protector ?? new MemoryOnlyProtector());
        if (_persistent)
        {
            Store.Load();
            Warning = Store.LoadWarning;
        }

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

        var gesture = _platform.SupportedGestures.Contains(Store.Settings.Hotkey)
            ? Store.Settings.Hotkey
            : _platform.DefaultGesture;
        if (_platform.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey) && !_platform.Hotkey.TryRegister(gesture))
            Warning ??= Strings.Get("HotkeyBusy");
    }

    public void TogglePause()
    {
        Paused = !Paused;
        Changed?.Invoke();
    }

    public void HistoryChanged()
    {
        Changed?.Invoke();
        if (!_persistent || Store.SavingBlocked)
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
            ToggleRequested?.Invoke();
        else
            Dispatcher.UIThread.Post(() => ToggleRequested?.Invoke());
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

    private sealed class MemoryOnlyProtector : IHistoryProtector
    {
        public byte[] Protect(byte[] clearBytes) => throw new InvalidOperationException();
        public byte[] Unprotect(byte[] protectedBytes) => throw new InvalidOperationException();
    }
}
