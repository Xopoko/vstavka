using Vstavka.Core;

namespace Vstavka.Platform.Linux;

public sealed class LinuxPlatformServices : IPlatformServices
{
    private ILinuxDesktop? _desktop;
    private readonly ClipboardAccess _clipboard;
    private readonly HotkeyAccess _hotkey;
    private readonly LinuxSession _session;
    private readonly Func<GnomeConnection> _connectShell;
    private readonly GnomeHelper? _helper;
    private GnomeConnection _shell;

    public LinuxPlatformServices()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            GnomeDesktop.TryConnect, X11Desktop.TryConnect, SecretServiceKeyStore.TryCreate,
            LinuxProcess.GetExecutable(), AppContext.BaseDirectory, LinuxProcess.Run)
    {
    }

    internal LinuxPlatformServices(Func<string, string?> environment, string home,
        Func<GnomeConnection> connectShell, Func<ILinuxDesktop?> connectX11, Func<IHistoryKeyStore?> keyStore,
        string? executable = null, string? bundledRoot = null, LinuxCommand? run = null)
    {
        _session = LinuxDesktop.DetectSession(environment);
        _connectShell = connectShell;
        _shell = connectShell();
        _desktop = LinuxDesktop.Connect(_session, _shell, connectX11);
        _clipboard = new ClipboardAccess(_desktop);
        _hotkey = new HotkeyAccess(_desktop);
        Paste = new PasteAccess(() => _desktop);
        PanelWindow = new PanelAccess(() => _desktop);

        var dataHome = HomeDirectory(environment("XDG_DATA_HOME"), Path.Combine(home, ".local", "share"));
        DataDirectory = Path.Combine(dataHome, "vstavka");
        Startup = new LinuxStartupRegistration(HomeDirectory(environment("XDG_CONFIG_HOME"), Path.Combine(home, ".config")), executable);
        if (_session == LinuxSession.Wayland && LinuxDesktop.IsGnome(environment))
            _helper = new GnomeHelper(dataHome, bundledRoot ?? AppContext.BaseDirectory, executable,
                run ?? LinuxProcess.Run, environment("XDG_DATA_DIRS"));

        var store = keyStore();
        if (store is not null)
            HistoryProtector = new AesGcmHistoryProtector(store);
    }

    private static string HomeDirectory(string? configured, string fallback) =>
        Path.IsPathFullyQualified(configured ?? "") ? configured! : fallback;

    public static bool IsSupported => OperatingSystem.IsLinux();
    public string Name => "Linux";
    public PlatformCapabilities Capabilities => (_desktop?.Capabilities ?? PlatformCapabilities.None) |
        (HistoryProtector is null ? PlatformCapabilities.None : PlatformCapabilities.PersistentHistory);
    public IReadOnlyList<PlatformNotice> Notices => _shell.Access switch
    {
        GnomeAccess.Denied => [new("gnome-extension-not-allowed", "Allow the Vstavka executable to use the GNOME Shell extension.")],
        GnomeAccess.Busy => [new("gnome-extension-busy", "Another Vstavka process is using the GNOME clipboard.")],
        _ when _helper?.NeedsRestart == true => [new("gnome-helper-restart", "Sign out and back in to finish.")],
        _ when _desktop is not null => [],
        _ when _session == LinuxSession.Wayland => [new("gnome-extension-missing", "Install and enable the Vstavka GNOME Shell extension.")],
        _ => [new("linux-desktop-unavailable", "Vstavka could not connect to a GNOME Shell extension or an X11 desktop.")],
    };
    public IReadOnlyList<PlatformPermission> Permissions
    {
        get
        {
            if (_helper is null)
                return [];
            RefreshGnome();
            return [_helper.Permission(_shell)];
        }
    }
    public IStartupRegistration Startup { get; }
    public string DataDirectory { get; }
    public string DefaultGesture => LinuxGesture.Suggestions[0];
    public IReadOnlyList<string> SupportedGestures => LinuxGesture.Suggestions;
    public IClipboardMonitor Clipboard => _clipboard;
    public IClipboardWriter ClipboardWriter => _clipboard;
    public IGlobalHotkey Hotkey => _hotkey;
    public IPasteService Paste { get; }
    public IPanelWindow PanelWindow { get; }
    public IHistoryProtector? HistoryProtector { get; }

    private void RefreshGnome()
    {
        _shell = _desktop is GnomeDesktop current ? current.RefreshAccess() : _connectShell();
        if (_shell.Access == GnomeAccess.SessionUnavailable || ReferenceEquals(_desktop, _shell.Desktop))
            return;
        var previous = _desktop;
        _desktop = _shell.Desktop;
        try
        {
            _clipboard.SetDesktop(_desktop);
            _hotkey.SetDesktop(_desktop);
        }
        finally
        {
            previous?.Dispose();
        }
    }

    public void Dispose()
    {
        _clipboard.Dispose();
        _hotkey.Dispose();
        _desktop?.Dispose();
    }

    private sealed class ClipboardAccess(ILinuxDesktop? desktop) : IClipboardMonitor, IClipboardWriter
    {
        private ILinuxDesktop? _desktop = desktop;
        private SynchronizationContext? _context;
        private volatile bool _started;
        private volatile bool _disposed;
        private int _suppress;

        public event Action<ClipboardCapture>? Captured;

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;
            _context = SynchronizationContext.Current;
            _started = true;
            Watch();
        }

        public void SetDesktop(ILinuxDesktop? desktop)
        {
            if (_desktop is not null)
                _desktop.ClipboardChanged -= Receive;
            _desktop = desktop;
            if (_started)
                Watch();
        }

        private void Watch()
        {
            if (_desktop is not { } desktop || !desktop.Capabilities.HasFlag(PlatformCapabilities.WatchClipboard))
                return;
            desktop.ClipboardChanged += Receive;
            try
            {
                desktop.StartWatching();
            }
            catch
            {
                desktop.ClipboardChanged -= Receive;
                _started = false;
                throw;
            }
        }

        public void SuppressNextChange() => Interlocked.Exchange(ref _suppress, 1);

        public void Write(ClipItem item, bool plainText)
        {
            // Each transport identifies its own writes by selection ownership. Do not
            // leave the manual suppression armed when a write produces no notification.
            Interlocked.Exchange(ref _suppress, 0);
            var (mime, data) = LinuxClipboard.Write(item, plainText);
            if (_desktop is not { } desktop)
                throw new NotSupportedException("Clipboard writing requires the GNOME Shell extension or an X11 display.");
            if (!desktop.Write(mime, data))
                throw new InvalidOperationException("Could not set the Linux clipboard.");
        }

        private void Receive(LinuxClipboardData payload)
        {
            if (!_started || payload.OwnWrite || Interlocked.Exchange(ref _suppress, 0) != 0)
                return;
            var capture = LinuxClipboard.Read(payload, LinuxClipboard.LoadIcon);
            if (capture is not null)
                Dispatch(_context, () =>
                {
                    if (_started)
                        Captured?.Invoke(capture);
                });
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_started && _desktop is { } desktop && desktop.Capabilities.HasFlag(PlatformCapabilities.WatchClipboard))
            {
                _started = false;
                desktop.ClipboardChanged -= Receive;
                desktop.StopWatching();
            }
        }
    }

    private sealed class HotkeyAccess : IGlobalHotkey
    {
        private ILinuxDesktop? _desktop;
        private LinuxGesture? _binding;
        private SynchronizationContext? _context;
        private volatile bool _registered;
        private volatile bool _disposed;

        public HotkeyAccess(ILinuxDesktop? desktop)
        {
            SetDesktop(desktop);
        }

        public event Action? Pressed;

        public void SetDesktop(ILinuxDesktop? desktop)
        {
            if (_desktop is not null)
                _desktop.HotkeyPressed -= Receive;
            _registered = false;
            _desktop = desktop;
            if (desktop is not null)
            {
                desktop.HotkeyPressed += Receive;
                _registered = _binding is not null && desktop.RegisterHotkey(_binding);
            }
        }

        public bool TryRegister(string gesture)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var binding = LinuxGesture.Parse(gesture);
            if (binding is null || _desktop is null || !_desktop.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey) ||
                !_desktop.RegisterHotkey(binding))
                return false;
            _context = SynchronizationContext.Current;
            _binding = binding;
            _registered = true;
            return true;
        }

        private void Receive()
        {
            if (_registered)
                Dispatch(_context, () =>
                {
                    if (_registered)
                        Pressed?.Invoke();
                });
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_desktop is null)
                return;
            _desktop.HotkeyPressed -= Receive;
            if (_registered)
            {
                _registered = false;
                _desktop.ReleaseHotkey();
            }
        }
    }

    private sealed class PasteAccess(Func<ILinuxDesktop?> desktop) : IPasteService
    {
        public PermissionState Permission => desktop()?.Capabilities.HasFlag(PlatformCapabilities.AutoPaste) == true
            ? PermissionState.NotNeeded : PermissionState.Missing;
        public void CaptureTarget() => desktop()?.CaptureTarget();
        public Task<bool> PasteAsync() => desktop()?.PasteAsync() ?? Task.FromResult(false);
        public void RequestPermission() { }
    }

    private sealed class PanelAccess(Func<ILinuxDesktop?> desktop) : IPanelWindow
    {
        public void Attach(nint handle, string descriptor) { }
        public void Showing() => desktop()?.PreparePanel();
        public void Hidden() { }
    }

    private static void Dispatch(SynchronizationContext? context, Action action)
    {
        if (context is null)
            ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), action);
        else
            context.Post(static state => ((Action)state!).Invoke(), action);
    }
}
