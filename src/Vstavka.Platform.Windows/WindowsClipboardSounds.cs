using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vstavka.Core;
using Vstavka.Services;

namespace Vstavka.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboardSounds : IClipboardSounds
{
    private const int LowLevelKeyboard = 13;
    private const uint KeyDown = 0x0100;
    private const uint KeyUp = 0x0101;
    private const uint SystemKeyDown = 0x0104;
    private const uint SystemKeyUp = 0x0105;
    private const uint Injected = 0x10;
    private readonly SoundService player = new();
    private readonly PasteShortcutState shortcut = new();
    private readonly KeyboardHook callback;
    private nint hook;
    private double? loadedVolume;

    public WindowsClipboardSounds(WindowsClipboard clipboard, Func<AppSettings> settings)
    {
        callback = HandleKeyboard;
        clipboard.CopyDetected += () => PlayCopy(settings());
    }

    public event Action? PasteShortcutPressed;

    public void Play(bool paste, double volume)
    {
        if (volume <= 0)
            return;
        if (loadedVolume != volume)
        {
            player.SetVolume(volume);
            loadedVolume = volume;
        }
        if (paste)
            player.Paste();
        else
            player.Copy();
    }

    private void PlayCopy(AppSettings settings)
    {
        if (settings.SoundEnabled && CountClipboardFormats() > 0)
            Play(paste: false, settings.Volume);
    }

    public void WatchPasteShortcuts(bool enabled)
    {
        if (enabled && hook == 0)
            hook = SetWindowsHookEx(LowLevelKeyboard, callback, NativeMethods.GetModuleHandle(null), 0);
        else if (!enabled && hook != 0)
        {
            UnhookWindowsHookEx(hook);
            hook = 0;
            shortcut.Reset();
        }
    }

    private nint HandleKeyboard(int code, nint message, nint data)
    {
        if (code == 0)
        {
            var key = Marshal.PtrToStructure<KeyboardEvent>(data);
            if (key.VirtualKey is PasteShortcutState.V or PasteShortcutState.Insert && (key.Flags & Injected) == 0)
            {
                if ((uint)message is KeyUp or SystemKeyUp)
                    shortcut.Release(key.VirtualKey);
                else if ((uint)message is KeyDown or SystemKeyDown &&
                         shortcut.Press(key.VirtualKey, ReadModifiers(), Environment.TickCount64))
                    PasteShortcutPressed?.Invoke();
            }
        }
        return CallNextHookEx(0, code, message, data);
    }

    private static HotkeyModifiers ReadModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (NativeMethods.GetAsyncKeyState(0x11) < 0) modifiers |= HotkeyModifiers.Ctrl;
        if (NativeMethods.GetAsyncKeyState(0x10) < 0) modifiers |= HotkeyModifiers.Shift;
        if (NativeMethods.GetAsyncKeyState(0x12) < 0) modifiers |= HotkeyModifiers.Alt;
        if (NativeMethods.GetAsyncKeyState(0x5B) < 0 || NativeMethods.GetAsyncKeyState(0x5C) < 0)
            modifiers |= HotkeyModifiers.Meta;
        return modifiers;
    }

    public void Dispose()
    {
        WatchPasteShortcuts(false);
        player.Dispose();
    }

    private delegate nint KeyboardHook(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardEvent
    {
        internal uint VirtualKey, ScanCode, Flags, Timestamp;
        internal nuint ExtraInfo;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", ExactSpelling = true)]
    private static extern nint SetWindowsHookEx(int type, KeyboardHook callback, nint module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern int CountClipboardFormats();
}

internal sealed class PasteShortcutState
{
    internal const uint V = 0x56;
    internal const uint Insert = 0x2D;
    private uint held;
    private long? lastPress;

    internal bool Press(uint key, HotkeyModifiers modifiers, long ticks)
    {
        var shortcut = (key, modifiers) is (V, HotkeyModifiers.Ctrl) or (Insert, HotkeyModifiers.Shift);
        if (!shortcut || held != 0)
            return false;
        held = key;
        if (lastPress is { } previous && ticks - previous <= 80)
            return false;
        lastPress = ticks;
        return true;
    }

    internal void Release(uint key)
    {
        if (held == key)
            held = 0;
    }

    internal void Reset()
    {
        held = 0;
        lastPress = null;
    }
}
