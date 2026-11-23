using Vstavka.Core;

namespace Vstavka.Platform.Linux;

public sealed class LinuxPlatformServices : IPlatformServices
{
    private readonly ILinuxDesktop? _desktop;
    private readonly ClipboardAccess _clipboard;
    private readonly HotkeyAccess _hotkey;

    public LinuxPlatformServices()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            GnomeDesktop.TryConnect, X11Desktop.TryConnect, SecretServiceKeyStore.TryCreate)
    {
    }

    internal LinuxPlatformServices(Func<string, string?> environment, string home,
        Func<GnomeConnection> connectShell, Func<ILinuxDesktop?> connectX11, Func<IHistoryKeyStore?> keyStore)
    {
        var session = LinuxDesktop.DetectSession(environment);
        var shell = connectShell();
        _desktop = LinuxDesktop.Connect(session, shell, connectX11);
        _clipboard = new ClipboardAccess(_desktop);
        _hotkey = new HotkeyAccess(_desktop);
        Paste = new PasteAccess(_desktop);
        PanelWindow = new PanelAccess(_desktop);

        var dataHome = environment("XDG_DATA_HOME");
        DataDirectory = Path.Combine(Path.IsPathFullyQualified(dataHome ?? "") ? dataHome! :
            Path.Combine(home, ".local", "share"), "vstavka");

        var notices = new List<PlatformNotice>();
        if (shell.Access == GnomeAccess.Denied)
        {
            notices.Add(new("gnome-extension-not-allowed",
                "Allow the Vstavka executable in the extension's allowed-executables setting with gsettings --schemadir, then restart Vstavka. See gnome-extension/README.md."));
        }
        else if (shell.Access == GnomeAccess.Busy)
        {
            notices.Add(new("gnome-extension-busy",
                "Another Vstavka process is using the GNOME clipboard. Close that process and restart Vstavka."));
        }
        else if (_desktop is null)
        {
            notices.Add(session == LinuxSession.Wayland
                ? new("gnome-extension-missing",
                    "Install and enable the Vstavka GNOME Shell extension, then restart Vstavka. See gnome-extension/README.md in the Vstavka repository.")
                : new("linux-desktop-unavailable", "Vstavka could not connect to a GNOME Shell extension or an X11 desktop."));
        }
        var store = keyStore();
        if (store is not null)
            HistoryProtector = new AesGcmHistoryProtector(store);
        Capabilities = (_desktop?.Capabilities ?? PlatformCapabilities.None) |
            (HistoryProtector is null ? PlatformCapabilities.None : PlatformCapabilities.PersistentHistory);
        Notices = notices.AsReadOnly();
    }

    public static bool IsSupported => OperatingSystem.IsLinux();
    public string Name => "Linux";
    public PlatformCapabilities Capabilities { get; }
    public IReadOnlyList<PlatformNotice> Notices { get; }
    public string DataDirectory { get; }
    public string DefaultGesture => LinuxGesture.Names[0];
    public IReadOnlyList<string> SupportedGestures => LinuxGesture.Names;
    public IClipboardMonitor Clipboard => _clipboard;
    public IClipboardWriter ClipboardWriter => _clipboard;
    public IGlobalHotkey Hotkey => _hotkey;
    public IPasteService Paste { get; }
    public IPanelWindow PanelWindow { get; }
    public IHistoryProtector? HistoryProtector { get; }

    public void Dispose()
    {
        _clipboard.Dispose();
        _hotkey.Dispose();
        _desktop?.Dispose();
    }

    private sealed class ClipboardAccess(ILinuxDesktop? desktop) : IClipboardMonitor, IClipboardWriter
    {
        private SynchronizationContext? _context;
        private volatile bool _started;
        private volatile bool _disposed;
        private int _suppress;

        public event Action<ClipboardCapture>? Captured;

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started || desktop is null || !desktop.Capabilities.HasFlag(PlatformCapabilities.WatchClipboard))
                return;
            _context = SynchronizationContext.Current;
            _started = true;
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
            if (desktop is null)
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
            if (_started && desktop is not null)
            {
                _started = false;
                desktop.ClipboardChanged -= Receive;
                desktop.StopWatching();
            }
        }
    }

    private sealed class HotkeyAccess : IGlobalHotkey
    {
        private readonly ILinuxDesktop? _desktop;
        private SynchronizationContext? _context;
        private volatile bool _registered;
        private volatile bool _disposed;

        public HotkeyAccess(ILinuxDesktop? desktop)
        {
            _desktop = desktop;
            if (_desktop is not null)
                _desktop.HotkeyPressed += Receive;
        }

        public event Action? Pressed;

        public bool TryRegister(string gesture)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var binding = LinuxGesture.Find(gesture);
            if (binding is null || _desktop is null || !_desktop.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey) ||
                !_desktop.RegisterHotkey(binding))
                return false;
            _context = SynchronizationContext.Current;
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

    private sealed class PasteAccess(ILinuxDesktop? desktop) : IPasteService
    {
        public PermissionState Permission => desktop?.Capabilities.HasFlag(PlatformCapabilities.AutoPaste) == true
            ? PermissionState.NotNeeded : PermissionState.Missing;
        public void CaptureTarget() => desktop?.CaptureTarget();
        public Task<bool> PasteAsync() => desktop?.PasteAsync() ?? Task.FromResult(false);
        public void RequestPermission() { }
    }

    private sealed class PanelAccess(ILinuxDesktop? desktop) : IPanelWindow
    {
        public void Attach(nint handle, string descriptor) { }
        public void Showing() => desktop?.PreparePanel();
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
