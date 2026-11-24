using System.Runtime.InteropServices;

namespace Vstavka.Platform.Windows;

internal interface IWindowsPaste
{
    nint ForegroundWindow { get; }
    uint CurrentProcessId { get; }
    uint ProcessId(nint window);
    bool RestoreForeground(nint window);
    bool ModifiersDown { get; }
    bool SendPaste();
}

internal sealed class WindowsPaste(IWindowsPaste native, Func<uint> clipboardSequence,
    Func<uint> writtenSequence, Func<int, Task>? delay = null) : IPasteService
{
    private readonly Func<int, Task> wait = delay ?? (milliseconds => Task.Delay(milliseconds));
    private (nint Window, uint ProcessId) target;
    private int pasting;

    public void CaptureTarget()
    {
        var foreground = native.ForegroundWindow;
        var process = native.ProcessId(foreground);
        target = foreground == 0 || process == 0 || process == native.CurrentProcessId ? default : (foreground, process);
    }

    public async Task<PasteResult> PasteAsync()
    {
        var sequence = writtenSequence();
        bool ClipboardUnchanged() => sequence != 0 && writtenSequence() == sequence && clipboardSequence() == sequence;
        PasteResult Failure() => ClipboardUnchanged() ? PasteResult.Manual : PasteResult.ClipboardChanged;

        if (Interlocked.Exchange(ref pasting, 1) != 0)
            return Failure();
        try
        {
            var (window, process) = target;
            bool StateStillCurrent() => target == (window, process) && native.ProcessId(window) == process && ClipboardUnchanged();

            if (window == 0 || !StateStillCurrent())
                return Failure();
            await wait(170);
            if (!StateStillCurrent() || !native.RestoreForeground(window))
                return Failure();
            for (var attempt = 0; attempt < 30 && (native.ModifiersDown || native.ForegroundWindow != window); attempt++)
            {
                if (!StateStillCurrent())
                    return Failure();
                await wait(20);
            }
            return native.ForegroundWindow == window && !native.ModifiersDown && StateStillCurrent() && native.SendPaste()
                ? PasteResult.ShortcutSent : Failure();
        }
        finally
        {
            Volatile.Write(ref pasting, 0);
        }
    }
}

internal sealed class Win32Paste : IWindowsPaste
{
    private static readonly int[] BlockingKeys = [0x10, 0x11, 0x12, 0x0D, 0x5B, 0x5C, 0x56];
    private static readonly NativeMethods.Input[] PasteKeys = [Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true)];

    public nint ForegroundWindow => NativeMethods.GetForegroundWindow();
    public uint CurrentProcessId => (uint)Environment.ProcessId;
    public uint ProcessId(nint window) => NativeMethods.ProcessId(window);
    public bool ModifiersDown => BlockingKeys.Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0);

    public bool RestoreForeground(nint window)
    {
        if (NativeMethods.IsIconic(window))
            NativeMethods.ShowWindowAsync(window, 9); // SW_RESTORE
        return ForegroundWindow == window || NativeMethods.SetForegroundWindow(window);
    }

    public bool SendPaste()
    {
        var size = Marshal.SizeOf<NativeMethods.Input>();
        var sent = NativeMethods.SendInput((uint)PasteKeys.Length, PasteKeys, size);
        // If only part of the sequence was accepted, release only the keys we pressed.
        if (sent is > 0 and < 4)
        {
            NativeMethods.Input[] release = sent == 2 ? [Key(0x56, true), Key(0x11, true)] : [Key(0x11, true)];
            NativeMethods.SendInput((uint)release.Length, release, size);
        }
        return sent == PasteKeys.Length;
    }

    private static NativeMethods.Input Key(ushort key, bool up = false) => new()
    {
        Type = 1,
        Data = new NativeMethods.InputData { Keyboard = new NativeMethods.KeyboardInput { Key = key, Flags = up ? 2u : 0u } }
    };
}
