using System.Text;
using Vstavka.Core;
using Vstavka.Platform;
using Vstavka.Platform.Linux;

Run("Session selects the extension first and never uses Xwayland as the clipboard fallback", TestModes);
Run("Snap enables an approved GNOME bridge or connects directly to X11", TestSnapSession);
Run("Clipboard maps supported formats, caps data and checks privacy markers", TestClipboardFormats);
Run("Clipboard and shortcut use their registration contexts", TestCallbacks);
Run("Linux shortcuts map usable gestures to GNOME accelerators and X11 keysyms", TestGestures);
Run("X11 recognizes terminal instance and class names for Ctrl+Shift+V", TestTerminalClasses);
Run("GNOME helper reflects installation, enablement and access, and offers GNOME sign-out only when available", TestGnomeHelper);
Run("XDG autostart follows the configured executable and honors Hidden", TestStartup);

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
        Check(platform.Capabilities == shell.Capabilities);
        Check(platform.DataDirectory == Path.Combine(environment["XDG_DATA_HOME"]!, "vstavka"));
        Check(platform.Paste.PasteAsync().Result == PasteResult.ShortcutSent);
        var encrypted = platform.HistoryProtector!.Protect("history"u8.ToArray());
        Check(platform.HistoryProtector.Unprotect(encrypted).AsSpan().SequenceEqual("history"u8));
    }
    Check(x11Attempts == 0);
    environment["XDG_DATA_HOME"] = "relative";
    using (var platform = Create(() => default, () => null))
    {
        Check(platform.Notices.Single().Code == "linux-desktop-unavailable");
        Check(platform.Permissions.Count == 0);
        Check(platform.Capabilities == PlatformCapabilities.None && platform.HistoryProtector is null);
        Check(platform.Paste.PasteAsync().Result == PasteResult.Manual);
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
    environment["XDG_CURRENT_DESKTOP"] = "GNOME";
    using var fallback = Create(() => default, () => null);
    Check(x11Attempts == 1 && fallback.Capabilities == x11.Capabilities);
    Check(fallback.Permissions.Count == 0);
}

static void TestSnapSession()
{
    var configHome = Directory.CreateTempSubdirectory("vstavka-snap-autostart-");
    try
    {
        var x11 = new FakeDesktop { Capabilities = PlatformCapabilities.WatchClipboard };
        var gnome = new FakeDesktop { Capabilities = PlatformCapabilities.WatchClipboard | PlatformCapabilities.GlobalHotkey };
        var session = "wayland";
        var desktop = "GNOME";
        GnomeConnection snapShell = default;
        var offeredExtension = false;
        var x11Attempts = 0;
        LinuxPlatformServices Create(string executable) => new(name => name switch
        {
            "SNAP_NAME" => "vstavka",
            "XDG_CONFIG_HOME" => configHome.FullName,
            "XDG_CURRENT_DESKTOP" => desktop,
            "XDG_SESSION_TYPE" => session,
            "DISPLAY" => ":55",
            _ => null,
        }, configHome.FullName, () => throw new InvalidOperationException("Snap tried to connect to GNOME Shell."),
            () => { x11Attempts++; return x11; }, () => null, executable,
            connectSnapShell: () => snapShell, openSnapHelper: () => offeredExtension = true);

        using (var wayland = Create("/snap/vstavka/1/app/Vstavka.Desktop"))
        {
            Check(wayland.Capabilities == PlatformCapabilities.None && x11Attempts == 0);
            var permission = wayland.Permissions.Single();
            Check(wayland.RequiresExplicitShortcut && permission.Code == "gnome-snap-helper" &&
                permission.State == PermissionState.Missing && wayland.Notices.Single().Code == "linux-snap-extension-needed");
            permission.Request!();
            Check(offeredExtension);
            snapShell = new(gnome);
            Check(wayland.Permissions.Single().State == PermissionState.Granted && wayland.Capabilities == gnome.Capabilities);
        }

        desktop = "KDE";
        using (var unsupported = Create("/snap/vstavka/1/app/Vstavka.Desktop"))
            Check(unsupported.Permissions.Count == 0 && !unsupported.RequiresExplicitShortcut &&
                unsupported.Notices.Single().Code == "linux-snap-x11-required");

        session = "x11";
        using (var first = Create("/snap/vstavka/1/app/Vstavka.Desktop"))
        {
            Check(first.Capabilities == PlatformCapabilities.WatchClipboard && x11Attempts == 1);
            Check(first.Notices.Single().Code == "linux-snap-history-session-only");
            Check(first.Startup.TrySetEnabled(true));
        }
        using var second = Create("/snap/vstavka/2/app/Vstavka.Desktop");
        Check(second.Startup.IsEnabled);
    }
    finally
    {
        configHome.Delete(recursive: true);
    }
}

static void TestClipboardFormats()
{
    byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMQMgn7DwAClAGcHVtGXwAAAABJRU5ErkJggg==");
    ClipboardCapture? Capture(string mime, byte[] bytes, string iconPath = "/some/editor.png") =>
        LinuxClipboard.Read(new(mime, bytes, "editor.desktop", "Editor", iconPath), _ => png);

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
    Check(LinuxClipboard.HasPrivateMarker(["Application/X-KeePassXC"]));
    Check(!LinuxClipboard.HasPrivateMarker([LinuxClipboard.Text]));
    Check(LinuxClipboard.IsPrivateHint(" SECRET \0"u8));
    Check(LinuxClipboard.IsPrivateHint([]));
    Check(LinuxClipboard.IsPrivateHint([0x80]));
    Check(!LinuxClipboard.IsPrivateHint("public"u8));
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
        Check(platform.DefaultGesture == "Ctrl+Alt+V");
        Check(platform.Hotkey.TryRegister("Shift+Meta+F12") && desktop.Accelerator == "<Shift><Super>F12");
        SynchronizationContext.SetSynchronizationContext(clipboardContext);
        desktop.AcceptHotkey = false;
        Check(!platform.Hotkey.TryRegister("Alt+V") && desktop.Accelerator == "<Shift><Super>F12");
        Check(!platform.Hotkey.TryRegister("Shift+V"));
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previous);
    }

    Task.Run(() =>
    {
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

static void TestGestures()
{
    foreach (var (text, accelerator, keysym, modifiers) in new (string, string, nuint, uint)[]
    {
        ("Ctrl+Alt+V", "<Control><Alt>v", 'v', 0x0c),
        ("Super+Shift+Space", "<Shift><Super>space", ' ', 0x41),
        ("Alt+0", "<Alt>0", '0', 0x08),
        ("Control+F1", "<Control>F1", 0xffbe, 0x04),
        ("Meta+Ctrl+Alt+Shift+F24", "<Control><Alt><Shift><Super>F24", 0xffd5, 0x4d),
    })
        Check(LinuxGesture.Parse(text) == new LinuxGesture(accelerator, keysym, modifiers));
    foreach (var invalid in new[] { "v", "Shift+V", "Ctrl+Ctrl+V", "Ctrl+F25", "Ctrl+Return", "Ctrl+Я" })
        Check(LinuxGesture.Parse(invalid) is null);
}

static void TestTerminalClasses()
{
    Check(LinuxTerminals.IsTerminalClass("gnome-terminal-server\0Gnome-terminal\0"));
    Check(LinuxTerminals.IsTerminalClass("custom-instance\0KiTtY\0"));
    Check(LinuxTerminals.IsTerminalClass("kitty\0custom-class\0"));
    foreach (var wmClass in new[] { "code\0Code\0", "xterm\0XTerm\0", "urxvt\0URxvt\0", "", null })
        Check(!LinuxTerminals.IsTerminalClass(wmClass));
}

static void TestGnomeHelper()
{
    const string uuid = "vstavka@xopoko.github.io";
    const string schema = "io.github.xopoko.Vstavka.Shell";
    const string executable = "/opt/Vstavka's clips/Vstavka.Desktop";
    const string original = """["/opt/Other's app", '/opt/with\\backslash']""";
    const string appended = """["/opt/Other's app", '/opt/with\\backslash', '/opt/Vstavka\'s clips/Vstavka.Desktop']""";
    var home = Directory.CreateTempSubdirectory("vstavka-helper-test-");
    try
    {
        var destination = Path.Combine(home.FullName, ".local", "share", "gnome-shell", "extensions", uuid);
        var schemas = Path.Combine(destination, "schemas");
        var allowed = original;
        const string disabled = "['first@example.org', 'second@example.org']";
        var enabled = disabled;
        var enabledWithHelper = $"['first@example.org', 'second@example.org', '{uuid}']";
        var sessionManagerAvailable = true;
        var compilerAvailable = false;
        var commands = new List<string[]>();
        var shell = default(GnomeConnection);
        string? RunCommand(string program, params string[] arguments)
        {
            commands.Add([program, .. arguments]);
            if (program == "glib-compile-schemas")
            {
                Check(File.Exists(Path.Combine(schemas, $"{schema}.gschema.xml")));
                Check(!File.Exists(Path.Combine(schemas, "gschemas.compiled")));
                if (!compilerAvailable)
                    return null;
                File.WriteAllText(Path.Combine(schemas, "gschemas.compiled"), "fake cache");
                return "";
            }
            if (program == "gdbus")
            {
                if (arguments[^2] == "org.gnome.SessionManager.Logout")
                    return "()";
                Check(arguments.SequenceEqual(["call", "--session", "--dest", "org.freedesktop.DBus", "--object-path",
                    "/org/freedesktop/DBus", "--method", "org.freedesktop.DBus.NameHasOwner", "org.gnome.SessionManager"]));
                return sessionManagerAvailable ? "(true,)\n" : "(false,)\n";
            }
            Check(program == "gsettings");
            if (arguments[0] == "--schemadir")
                return arguments[2] == "get" ? allowed : allowed = arguments[^1];
            return arguments[0] == "get" ? enabled : enabled = arguments[^1];
        }

        LinuxPlatformServices CreatePlatform() => new(name => name switch
        {
            "XDG_SESSION_TYPE" => "wayland",
            "XDG_CURRENT_DESKTOP" => "ubuntu:GNOME",
            _ => null,
        }, home.FullName, () => shell, () => throw new InvalidOperationException("X11 fallback"), () => null,
            executable, AppContext.BaseDirectory, RunCommand);
        using var platform = CreatePlatform();
        var missing = platform.Permissions.Single();
        Check(missing is { Code: "gnome-helper", Requirement: PermissionRequirement.Required, State: PermissionState.Missing, Request: not null });
        Check(platform.Notices.Single().Code == "gnome-extension-missing");
        platform.Clipboard.Start();
        try
        {
            missing.Request!();
            throw new InvalidOperationException("GNOME helper setup succeeded without compiled schemas.");
        }
        catch (IOException) { }
        Check(!File.Exists(Path.Combine(destination, "metadata.json")) && commands.Count == 1);
        Check(platform.Permissions.Single() is { State: PermissionState.Missing, Request: not null });
        commands.Clear();
        compilerAvailable = true;
        Task.WhenAll(Task.Run(missing.Request!), Task.Run(missing.Request!)).GetAwaiter().GetResult();
        Check(File.Exists(Path.Combine(destination, "metadata.json")) && File.Exists(Path.Combine(destination, "extension.js")));
        Check(commands.Count == 6 && enabled == enabledWithHelper);
        Check(commands[0].SequenceEqual(["glib-compile-schemas", "--strict", schemas]));
        Check(commands[1].SequenceEqual(["gsettings", "--schemadir", schemas, "get", schema, "allowed-executables"]));
        Check(commands[2].SequenceEqual(["gsettings", "--schemadir", schemas, "set", schema, "allowed-executables", appended]));
        Check(commands[3].SequenceEqual(["gsettings", "get", "org.gnome.shell", "enabled-extensions"]));
        Check(commands[4].SequenceEqual(["gsettings", "set", "org.gnome.shell", "enabled-extensions", enabledWithHelper]));
        Check(commands[5].SequenceEqual(commands[3]));

        var notice = platform.Notices.Single();
        var restart = platform.Permissions.Single();
        Check(restart is { State: PermissionState.RestartRequired, Request: not null });
        Check(notice.Code == "gnome-helper-restart" && notice.Resolve == restart.Request);
        commands.Clear();
        restart.Request!();
        Check(commands.Single().SequenceEqual(["gdbus", "call", "--session", "--dest", "org.gnome.SessionManager",
            "--object-path", "/org/gnome/SessionManager", "--method", "org.gnome.SessionManager.Logout", "0"]));

        using (var reopened = CreatePlatform())
            Check(reopened.Permissions.Single() is { State: PermissionState.RestartRequired, Request: not null });
        sessionManagerAvailable = false;
        Check(platform.Permissions.Single() is { State: PermissionState.RestartRequired, Request: null });
        Check(platform.Notices.Single() is { Code: "gnome-helper-restart", Resolve: null });

        sessionManagerAvailable = true;
        enabled = disabled;
        var enable = platform.Permissions.Single();
        Check(enable is { State: PermissionState.Missing, Request: not null });
        commands.Clear();
        enable.Request!();
        Check(commands.Count == 2 && enabled == enabledWithHelper);
        Check(commands[0].SequenceEqual(["gsettings", "get", "org.gnome.shell", "enabled-extensions"]));
        Check(commands[1].SequenceEqual(["gsettings", "set", "org.gnome.shell", "enabled-extensions", enabledWithHelper]));
        Check(platform.Permissions.Single() is { State: PermissionState.RestartRequired, Request: not null });

        shell = new(null, GnomeAccess.SessionUnavailable);
        Check(platform.Permissions.Single() is { State: PermissionState.Missing, Request: null });

        shell = new(null, GnomeAccess.Denied);
        var allow = platform.Permissions.Single();
        Check(allow is { State: PermissionState.Missing, Request: not null });
        commands.Clear();
        allowed = original;
        allow.Request!();
        Check(commands.Count == 2 && allowed == appended);
        allow.Request!();
        Check(commands.Count == 3 && commands[^1].SequenceEqual(commands[0]));

        var desktop = new FakeDesktop();
        shell = new(desktop);
        Check(platform.Permissions.Single() is { State: PermissionState.Granted, Request: null });
        Check(platform.Notices.Count == 0 && platform.Capabilities == desktop.Capabilities && desktop.Starts == 1);
        shell = new(null, GnomeAccess.Busy);
        Check(platform.Permissions.Single() is { State: PermissionState.Granted, Request: null });
    }
    finally
    {
        home.Delete(recursive: true);
    }
}

static void TestStartup()
{
    var home = Directory.CreateTempSubdirectory("vstavka-autostart-test-");
    try
    {
        var configHome = Path.Combine(home.FullName, "xdg");
        const string executable = "/opt/Vstavka Clips/Vstavka.Desktop";
        using var platform = new LinuxPlatformServices(name => name == "XDG_CONFIG_HOME" ? configHome : null,
            home.FullName, () => default, () => null, () => null, executable);
        var startup = platform.Startup;
        var entry = Path.Combine(configHome, "autostart", "io.github.xopoko.Vstavka.desktop");
        Check(startup.IsSupported && !startup.IsEnabled);
        Check(startup.TrySetEnabled(true) && startup.IsEnabled);
        Check(File.ReadAllLines(entry).SequenceEqual([
            "[Desktop Entry]", "Type=Application", "Name=Vstavka",
            $"""Exec="{executable}" --background""", "X-GNOME-Autostart-enabled=true",
        ]));
        File.AppendAllText(entry, "Hidden=true\n");
        Check(!startup.IsEnabled);
        File.WriteAllText(entry, $"[Desktop Entry]\nExec={executable} --background\n");
        Check(!startup.IsEnabled);
        File.WriteAllText(entry, $"""
            [Desktop Entry]
            Exec="{executable}.other" --background
            [Desktop Action Another]
            Exec="{executable}" --background
            """);
        Check(!startup.IsEnabled);
        Check(startup.TrySetEnabled(false) && !startup.IsEnabled && !File.Exists(entry));
    }
    finally
    {
        home.Delete(recursive: true);
    }
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
    public PlatformCapabilities Capabilities { get; init; } = PlatformCapabilities.WatchClipboard | PlatformCapabilities.GlobalHotkey;
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
