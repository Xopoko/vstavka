using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static Vstavka.Platform.Linux.X11Native;

namespace Vstavka.Platform.Linux;

internal sealed partial class X11Desktop : ILinuxDesktop
{
    private readonly nint _display;
    private readonly nint _xcb;
    private readonly nuint _root;
    private readonly nuint _window;
    private readonly int _fixesEvent;
    private readonly bool _hasXTest;
    private readonly byte _v;
    private readonly byte _control;
    private readonly uint _numLock;
    private readonly uint _alt;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wakeup = new(false);
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly object _lifetime = new();
    private readonly Dictionary<string, nuint> _atoms = new(StringComparer.Ordinal);
    private volatile bool _closed;
    private bool _watching;
    private uint? _hotkey;
    private nuint _pasteTarget;

    private X11Desktop(nint display, int fixesEvent, bool hasXTest)
    {
        _display = display;
        _xcb = XGetXCBConnection(display);
        _fixesEvent = fixesEvent;
        _hasXTest = hasXTest;
        _root = XDefaultRootWindow(display);
        _window = CreateHiddenWindow();
        _v = XKeysymToKeycode(display, 'v');
        _control = XKeysymToKeycode(display, 0xffe3); // Control_L
        _numLock = ModifierMask(0xff7f, 0); // Num_Lock
        _alt = ModifierMask(0xffe9, 1 << 3); // Alt_L
        _thread = new Thread(Run) { Name = "Vstavka X11", IsBackground = true };
        _thread.Start();
    }

    public static X11Desktop? TryConnect()
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess)
            return null;
        nint display = 0;
        try
        {
            display = XOpenDisplay(0);
            if (display == 0)
                return null;
            var fixes = -1;
            try
            {
                if (XFixesQueryExtension(display, out var eventBase, out _) != 0 &&
                    XFixesQueryVersion(display, out var major, out _) != 0 && major >= 1)
                    fixes = eventBase;
            }
            catch (DllNotFoundException) { }
            var xTest = false;
            try
            {
                xTest = XTestQueryExtension(display, out _, out _, out _, out _) != 0;
            }
            catch (DllNotFoundException) { }
            var backend = new X11Desktop(display, fixes, xTest);
            display = 0; // The worker owns the connection.
            return backend;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (display != 0)
                XCloseDisplay(display);
        }
    }

    public PlatformCapabilities Capabilities =>
        (_fixesEvent >= 0 ? PlatformCapabilities.WatchClipboard : PlatformCapabilities.None) |
        (_v != 0 ? PlatformCapabilities.GlobalHotkey : PlatformCapabilities.None) |
        (_hasXTest && _control != 0 && _v != 0 ? PlatformCapabilities.AutoPaste : PlatformCapabilities.None);

    public event Action<LinuxClipboardData>? ClipboardChanged;
    public event Action? HotkeyPressed;

    public void StartWatching() => Invoke(() =>
    {
        if (_fixesEvent >= 0 && !_watching)
        {
            _watching = true;
            XFixesSelectSelectionInput(_display, _window, Atom("CLIPBOARD"), 7);
            XFlush(_display);
        }
    });

    public void StopWatching() => Invoke(() =>
    {
        if (_watching)
        {
            _watching = false;
            XFixesSelectSelectionInput(_display, _window, Atom("CLIPBOARD"), 0);
            XFlush(_display);
            FinishRead();
        }
    });

    public bool Write(string mime, byte[] data) => Invoke(() => OwnClipboard(mime, data));

    public bool RegisterHotkey(LinuxGesture gesture) => Invoke(() =>
    {
        const uint mod1 = 1 << 3;
        var modifiers = (gesture.XModifiers & mod1) == 0 ? gesture.XModifiers : (gesture.XModifiers & ~mod1) | _alt;
        if (_hotkey == modifiers)
            return true;
        var grabbed = new List<uint>();
        foreach (var mask in LockMasks(modifiers))
        {
            // X11 GrabKey with a checked reply so a competing shortcut cannot
            // trigger Xlib's process-wide error handler.
            if (!Checked(xcb_grab_key_checked(_xcb, 0, (uint)_root, (ushort)mask, _v, 1, 1)))
            {
                foreach (var old in grabbed)
                    XUngrabKey(_display, _v, old, _root);
                XFlush(_display);
                return false;
            }
            grabbed.Add(mask);
        }
        Ungrab();
        _hotkey = modifiers;
        return true;
    });

    public void ReleaseHotkey() => Invoke(Ungrab);

    private void Ungrab()
    {
        if (_hotkey is not { } modifiers)
            return;
        foreach (var mask in LockMasks(modifiers))
            XUngrabKey(_display, _v, mask, _root);
        XFlush(_display);
        _hotkey = null;
    }

    private IEnumerable<uint> LockMasks(uint mods)
    {
        const uint caps = 1 << 1;
        return new[] { mods, mods | caps, mods | _numLock, mods | caps | _numLock }.Distinct();
    }

    private uint ModifierMask(nuint keysym, uint fallback)
    {
        var code = XKeysymToKeycode(_display, keysym);
        var pointer = XGetModifierMapping(_display);
        if (pointer == 0)
            return fallback;
        try
        {
            var map = Marshal.PtrToStructure<ModifierMap>(pointer);
            for (var index = 0; code != 0 && index < map.KeysPerModifier * 8; index++)
                if (Marshal.ReadByte(map.Keycodes, index) == code)
                    return 1u << (index / map.KeysPerModifier);
            return fallback;
        }
        finally
        {
            XFreeModifiermap(pointer);
        }
    }

    public void CaptureTarget() => Invoke(() => { XGetInputFocus(_display, out _pasteTarget, out _); });
    public void PreparePanel() { }

    public Task<bool> PasteAsync() => Enqueue(() =>
    {
        var target = _pasteTarget;
        _pasteTarget = 0;
        if (!Capabilities.HasFlag(PlatformCapabilities.AutoPaste) || target <= 1 || target == _root || target == _window)
            return false;
        XGetInputFocus(_display, out var focus, out _);
        if (focus != target &&
            (!Checked(xcb_set_input_focus_checked(_xcb, 2, (uint)target, 0)) ||
             XGetInputFocus(_display, out focus, out _) == 0 || focus != target))
            return false;
        var success = XTestFakeKeyEvent(_display, _control, 1, 0) != 0;
        success &= XTestFakeKeyEvent(_display, _v, 1, 0) != 0;
        success &= XTestFakeKeyEvent(_display, _v, 0, 0) != 0;
        success &= XTestFakeKeyEvent(_display, _control, 0, 0) != 0;
        XFlush(_display);
        return success;
    });

    private void Run()
    {
        try
        {
            while (!_closed)
            {
                while (_commands.TryDequeue(out var action))
                    action();
                // Bound each batch so continuous X11 events cannot starve commands.
                for (var count = 0; count < 64 && XPending(_display) > 0; count++)
                {
                    XNextEvent(_display, out var xevent);
                    if (xevent.Header.Type == KeyPress && _hotkey is { } mods && xevent.Key.Keycode == _v &&
                        (xevent.Key.State & ~(_numLock | (1u << 1))) == mods)
                        HotkeyPressed?.Invoke();
                    else
                        HandleClipboardEvent(xevent);
                }
                ExpireTransfers();
                _wakeup.WaitOne(10);
            }
        }
        finally
        {
            lock (_lifetime)
            {
                _closed = true;
                while (_commands.TryDequeue(out var action))
                    action();
            }
            FinishRead();
            XCloseDisplay(_display);
            _wakeup.Dispose();
        }
    }

    private Task<T> Enqueue<T>(Func<T> operation)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lifetime)
        {
            if (_closed)
                return Task.FromException<T>(new ObjectDisposedException(nameof(X11Desktop)));
            _commands.Enqueue(() =>
            {
                try
                {
                    result.SetResult(operation());
                }
                catch (Exception error)
                {
                    result.SetException(error);
                }
            });
            _wakeup.Set();
        }
        return result.Task;
    }

    private T Invoke<T>(Func<T> operation) => Enqueue(operation).GetAwaiter().GetResult();
    private void Invoke(Action operation) => Invoke(() => { operation(); return true; });

    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_closed)
                return;
            _closed = true;
            _wakeup.Set();
        }
        _thread.Join();
    }

    private nuint CreateHiddenWindow() => XCreateSimpleWindow(_display, _root, 0, 0, 1, 1, 0, 0, 0);

    private nuint Atom(string name)
    {
        if (!_atoms.TryGetValue(name, out var atom))
        {
            atom = XInternAtom(_display, name, 0);
            _atoms.Add(name, atom);
        }
        return atom;
    }

    private bool Checked(uint cookie)
    {
        var error = xcb_request_check(_xcb, cookie);
        if (error == 0)
            return true;
        free(error);
        return false;
    }
}
