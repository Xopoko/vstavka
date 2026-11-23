using System.Text;
using Vstavka.Core;
using Vstavka.Platform;
using Vstavka.Platform.Linux;

Run("Session selects the extension first and never uses Xwayland as the clipboard fallback", TestModes);
Run("Clipboard maps supported formats, caps data and skips private selections", TestClipboardFormats);
Run("Clipboard and shortcut use their registration contexts and keep external copies after a write", TestCallbacks);

static void TestModes()
{
    var shell = new FakeDesktop();
    var x11 = new FakeDesktop { Capabilities = PlatformCapabilities.WatchClipboard };
    KeyStore key = new();
    var environment = new Dictionary<string, string?>
    {
        ["XDG_SESSION_TYPE"] = "wayland", ["DISPLAY"] = ":55",
        ["XDG_DATA_HOME"] = Path.GetFullPath("example-data"),
    };
    var home = Path.GetFullPath("example-home");
    var x11Attempts = 0;
    LinuxPlatformServices Create(Func<GnomeConnection> connectShell, Func<IHistoryKeyStore?> keyStore) =>
        new(name => environment.GetValueOrDefault(name), home, connectShell, () => { x11Attempts++; return x11; }, keyStore);

    using (var platform = Create(() => new(shell), () => key))
    {
        Check(platform.Capabilities == (shell.Capabilities | PlatformCapabilities.PersistentHistory));
        Check(platform.DataDirectory == Path.Combine(environment["XDG_DATA_HOME"]!, "vstavka"));
        var encrypted = platform.HistoryProtector!.Protect("history"u8.ToArray());
        Check(platform.HistoryProtector.Unprotect(encrypted).AsSpan().SequenceEqual("history"u8));
    }
    Check(x11Attempts == 0);
    environment["XDG_DATA_HOME"] = "relative";
    using (var platform = Create(() => default, () => null))
    {
        Check(platform.Notices.Single().Code == "gnome-extension-missing");
        Check(platform.Capabilities == PlatformCapabilities.None && platform.HistoryProtector is null);
        Check(platform.Paste.Permission == PermissionState.Missing && !platform.Paste.PasteAsync().Result);
        Check(platform.DataDirectory == Path.Combine(home, ".local", "share", "vstavka"));
    }
    using (var platform = Create(() => new(null, GnomeAccess.Denied), () => null))
    {
        Check(platform.Notices.Single().Code == "gnome-extension-not-allowed");
        Check(platform.Capabilities == PlatformCapabilities.None);
    }
    using (var platform = Create(() => new(null, GnomeAccess.Busy), () => null))
    {
        Check(platform.Notices.Single().Code == "gnome-extension-busy");
        Check(platform.Capabilities == PlatformCapabilities.None);
    }
    Check(x11Attempts == 0);
    environment.Remove("XDG_SESSION_TYPE");
    using var fallback = Create(() => default, () => null);
    Check(x11Attempts == 1 && fallback.Capabilities == x11.Capabilities);
}

static void TestClipboardFormats()
{
    byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMQMgn7DwAClAGcHVtGXwAAAABJRU5ErkJggg==");
    ClipboardCapture? Capture(string mime, byte[] bytes, IReadOnlyList<string>? formats = null, string? hint = null,
        string iconPath = "/some/editor.png") =>
        LinuxClipboard.Read(new(mime, bytes, "editor.desktop", "Editor", iconPath, formats, hint), _ => png);

    var link = Capture("UTF8_STRING", Encode("https://example.com/"));
    Check(link?.Item is { Kind: ClipKind.Link, SourceApp: "Editor" } &&
        link.Source is { Id: "editor.desktop", Name: "Editor", IconPng: not null });
    Check(Capture("STRING", [0xe9])?.Item.Text == "é");
    Check(Capture(LinuxClipboard.Text, [0x80]) is null);
    Check(Capture(LinuxClipboard.Text, Encode("#adf"), iconPath: "/some/editor.svg") is
        { Item.Kind: ClipKind.Color, Source.IconPng: null });
    Check(Capture(LinuxClipboard.Image, png)?.Item.ImageBytes == png);
    Check(Capture(LinuxClipboard.Image, new byte[HistoryStore.MaxImageBytes + 1]) is null);
    var files = Capture(LinuxClipboard.Files, Encode(
        "# Comment\r\nfile:///home/test/a%20b\r\nfile://localhost/home/test/c\r\nfile://remote/share/d\r\nhttps://example.com/"));
    Check(files?.Item.Kind == ClipKind.Files && files.Item.Files.SequenceEqual(["/home/test/a b", "/home/test/c"]));
    Check(Capture(LinuxClipboard.Text, Encode("private"), ["application/x-keepassxc"]) is null);
    Check(Capture(LinuxClipboard.Text, Encode("private"), [LinuxClipboard.PasswordHint], " secret ") is null);
    Check(Capture(LinuxClipboard.Text, Encode("unknown"), [LinuxClipboard.PasswordHint]) is null);
    Check(Capture(LinuxClipboard.Text, Encode("normal"), [LinuxClipboard.PasswordHint], "public") is not null);
}

static void TestCallbacks()
{
    var desktop = new FakeDesktop();
    using var platform = new LinuxPlatformServices(_ => null, Path.GetFullPath("."), () => new(desktop), () => null, () => null);
    var clipboardContext = new QueuedContext();
    var shortcutContext = new QueuedContext();
    var previous = SynchronizationContext.Current;
    var clips = new List<string>();
    var presses = 0;
    platform.Clipboard.Captured += capture => clips.Add(capture.Item.Text);
    platform.Hotkey.Pressed += () => presses++;
    try
    {
        SynchronizationContext.SetSynchronizationContext(clipboardContext);
        platform.Clipboard.Start();
        platform.Clipboard.Start();
        SynchronizationContext.SetSynchronizationContext(shortcutContext);
        Check(platform.DefaultGesture == "Ctrl+Shift+V");
        Check(platform.SupportedGestures.SequenceEqual(["Ctrl+Shift+V", "Ctrl+Alt+V", "Alt+V"]));
        Check(platform.Hotkey.TryRegister("Ctrl+Alt+V") && desktop.Accelerator == "<Control><Alt>v");
        SynchronizationContext.SetSynchronizationContext(clipboardContext);
        desktop.AcceptHotkey = false;
        Check(!platform.Hotkey.TryRegister("Alt+V") && desktop.Accelerator == "<Control><Alt>v");
        Check(!platform.Hotkey.TryRegister("Ctrl+V"));
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previous);
    }

    Task.Run(() =>
    {
        platform.Clipboard.SuppressNextChange();
        desktop.Copy("explicitly suppressed");
        platform.Clipboard.SuppressNextChange();
        platform.ClipboardWriter.Write(new ClipItem { Kind = ClipKind.Files, Files = ["/tmp/a", "/tmp/b"] }, false);
        desktop.Copy("external copy");
        desktop.Press();
    }).GetAwaiter().GetResult();
    Check(desktop.Starts == 1 && clips.Count == 0 && presses == 0);
    clipboardContext.Drain();
    Check(clips.SequenceEqual(["external copy"]) && presses == 0);
    shortcutContext.Drain();
    Check(presses == 1 && desktop.Written == (LinuxClipboard.Text, "/tmp/a\n/tmp/b"));
}

static void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
}

static void Check(bool value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? condition = null)
{
    if (!value)
        throw new InvalidOperationException($"Assertion failed: {condition}");
}

sealed class FakeDesktop : ILinuxDesktop
{
    public PlatformCapabilities Capabilities { get; init; } = PlatformCapabilities.WatchClipboard |
        PlatformCapabilities.GlobalHotkey | PlatformCapabilities.AutoPaste | PlatformCapabilities.PanelPlacement;
    public int Starts { get; private set; }
    public bool AcceptHotkey { get; set; } = true;
    public string? Accelerator { get; private set; }
    public (string Mime, string Text) Written { get; private set; }
    public event Action<LinuxClipboardData>? ClipboardChanged;
    public event Action? HotkeyPressed;
    public void Copy(string text) => ClipboardChanged?.Invoke(new(LinuxClipboard.Text, Encoding.UTF8.GetBytes(text)));
    public void Press() => HotkeyPressed?.Invoke();
    public void StartWatching() => Starts++;
    public void StopWatching() { }
    public bool Write(string mime, byte[] data)
    {
        Written = (mime, Encoding.UTF8.GetString(data));
        ClipboardChanged?.Invoke(new(mime, data, OwnWrite: true));
        return true;
    }
    public bool RegisterHotkey(LinuxGesture gesture)
    {
        if (!AcceptHotkey)
            return false;
        Accelerator = gesture.Accelerator;
        return true;
    }
    public void ReleaseHotkey() => Accelerator = null;
    public void CaptureTarget() { }
    public Task<bool> PasteAsync() => Task.FromResult(true);
    public void PreparePanel() { }
    public void Dispose() { }
}

sealed class QueuedContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
    public void Drain()
    {
        while (_queue.TryDequeue(out var work))
            work.Callback(work.State);
    }
}

sealed class KeyStore : IHistoryKeyStore
{
    private byte[]? _key;
    public byte[]? TryLoadKey() => _key?.ToArray();
    public void SaveKey(byte[] key) => _key = key.ToArray();
}
