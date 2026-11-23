using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vstavka.Core;

namespace Vstavka.Platform.MacOS;

internal sealed class MacGlobalHotkey(Func<uint, uint, Action, IDisposable?> register) : IGlobalHotkey
{
    internal const string DefaultGesture = "Shift+Meta+V";
    internal static IReadOnlyList<string> SupportedGestures { get; } = Array.AsReadOnly([DefaultGesture, "Ctrl+Meta+V", "Alt+Meta+V"]);

    private Binding? current;
    private bool disposed;

    public event Action? Pressed;

    public bool TryRegister(string gesture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!HotkeyGesture.TryParse(gesture, out var selection) || !CarbonHotkey.TryGetKeyCode(selection.Key, out var keyCode))
            return false;
        var context = SynchronizationContext.Current;
        if (current is { } existing && existing.Gesture == selection)
        {
            existing.Context = context;
            return true;
        }

        var binding = new Binding(selection, context);
        var registration = register(keyCode, CarbonHotkey.GetModifiers(selection.Modifiers), () => Dispatch(binding));
        if (registration is null)
            return false;
        binding.Registration = registration;
        var previous = current;
        current = binding;
        previous?.Registration?.Dispose();
        return true;
    }

    private void Dispatch(Binding binding)
    {
        if (current != binding)
            return;
        var context = binding.Context;
        if (context is null)
            Pressed?.Invoke();
        else
            context.Post(_ =>
            {
                if (current == binding && context == binding.Context)
                    Pressed?.Invoke();
            }, null);
    }

    public void Dispose()
    {
        disposed = true;
        var active = current;
        current = null;
        active?.Registration?.Dispose();
        Pressed = null;
    }

    private sealed class Binding(HotkeyGesture gesture, SynchronizationContext? context)
    {
        internal HotkeyGesture Gesture { get; } = gesture;
        internal SynchronizationContext? Context { get; set; } = context;
        internal IDisposable? Registration { get; set; }
    }
}

internal sealed partial class CarbonHotkey : IDisposable
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint KeyboardEventClass = 0x6b657962; // 'keyb'
    private const uint HotkeyPressed = 5;
    private const uint DirectObject = 0x2d2d2d2d; // '----'
    private const uint HotkeyIdType = 0x686b6964; // 'hkid'
    private const uint Signature = 0x5653544b; // 'VSTK'
    private const uint Exclusive = 1;
    private const int EventNotHandled = -9874;

    private const uint CmdKey = 1 << 8;
    private const uint ShiftKey = 1 << 9;
    private const uint OptionKey = 1 << 11;
    private const uint ControlKey = 1 << 12;

    internal const ushort KeyCodeV = 0x09;

    // HIToolbox kVK codes. ANSI letters and digits identify physical US keyboard positions.
    private static readonly Dictionary<string, uint> KeyCodes = new(StringComparer.Ordinal)
    {
        ["A"] = 0x00, ["B"] = 0x0B, ["C"] = 0x08, ["D"] = 0x02, ["E"] = 0x0E,
        ["F"] = 0x03, ["G"] = 0x05, ["H"] = 0x04, ["I"] = 0x22, ["J"] = 0x26,
        ["K"] = 0x28, ["L"] = 0x25, ["M"] = 0x2E, ["N"] = 0x2D, ["O"] = 0x1F,
        ["P"] = 0x23, ["Q"] = 0x0C, ["R"] = 0x0F, ["S"] = 0x01, ["T"] = 0x11,
        ["U"] = 0x20, ["V"] = KeyCodeV, ["W"] = 0x0D, ["X"] = 0x07, ["Y"] = 0x10, ["Z"] = 0x06,
        ["0"] = 0x1D, ["1"] = 0x12, ["2"] = 0x13, ["3"] = 0x14, ["4"] = 0x15,
        ["5"] = 0x17, ["6"] = 0x16, ["7"] = 0x1A, ["8"] = 0x1C, ["9"] = 0x19,
        ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76, ["F5"] = 0x60,
        ["F6"] = 0x61, ["F7"] = 0x62, ["F8"] = 0x64, ["F9"] = 0x65, ["F10"] = 0x6D,
        ["F11"] = 0x67, ["F12"] = 0x6F, ["F13"] = 0x69, ["F14"] = 0x6B, ["F15"] = 0x71,
        ["F16"] = 0x6A, ["F17"] = 0x40, ["F18"] = 0x4F, ["F19"] = 0x50, ["F20"] = 0x5A,
        ["Space"] = 0x31,
    };

    private static readonly ConcurrentDictionary<uint, Action> Handlers = new();
    private static int nextId;

    private readonly uint id;
    private nint handler;
    private nint registration;
    private CarbonHotkey(uint id) => this.id = id;

    internal static bool TryGetKeyCode(string key, out uint code) => KeyCodes.TryGetValue(key, out code);

    internal static uint GetModifiers(HotkeyModifiers modifiers)
    {
        uint flags = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
            flags |= ControlKey;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
            flags |= OptionKey;
        if (modifiers.HasFlag(HotkeyModifiers.Shift))
            flags |= ShiftKey;
        if (modifiers.HasFlag(HotkeyModifiers.Meta))
            flags |= CmdKey;
        return flags;
    }

    internal static unsafe CarbonHotkey? TryRegister(uint keyCode, uint modifiers, Action pressed)
    {
        var id = unchecked((uint)Interlocked.Increment(ref nextId));
        var binding = new CarbonHotkey(id);
        var target = GetApplicationEventTarget();
        var type = new EventType(KeyboardEventClass, HotkeyPressed);
        var status = InstallEventHandler(target, &HandleEvent, 1, &type, (nint)id, out binding.handler);
        if (status != 0)
            return null;

        status = RegisterEventHotKey(keyCode, modifiers, new HotkeyId(Signature, id), target, Exclusive,
            out binding.registration);
        if (status != 0)
        {
            binding.Dispose();
            return null;
        }
        Handlers.TryAdd(id, pressed);
        return binding;
    }

    public void Dispose()
    {
        Handlers.TryRemove(id, out _);
        if (registration != 0)
            UnregisterEventHotKey(Interlocked.Exchange(ref registration, 0));
        if (handler != 0)
            RemoveEventHandler(Interlocked.Exchange(ref handler, 0));
    }

    [UnmanagedCallersOnly]
    private static int HandleEvent(nint next, nint nativeEvent, nint userData)
    {
        var status = GetEventParameter(nativeEvent, DirectObject, HotkeyIdType, 0, 8, 0, out var identifier);
        if (status != 0 || identifier.Signature != Signature || identifier.Id != (uint)userData ||
            !Handlers.TryGetValue(identifier.Id, out var pressed))
            return EventNotHandled;
        try
        {
            pressed();
            return 0;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("macOS hotkey callback failed: {0}", error.GetType().Name);
            return EventNotHandled;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct HotkeyId(uint Signature, uint Id);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct EventType(uint Class, uint Kind);

    [LibraryImport(Carbon)]
    private static partial nint GetApplicationEventTarget();
    [LibraryImport(Carbon)]
    private static unsafe partial int InstallEventHandler(nint target, delegate* unmanaged<nint, nint, nint, int> callback,
        uint count, EventType* types, nint userData, out nint handler);
    [LibraryImport(Carbon)]
    private static partial int RemoveEventHandler(nint handler);
    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(uint keyCode, uint modifiers, HotkeyId identifier, nint target,
        uint options, out nint registration);
    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(nint registration);
    [LibraryImport(Carbon)]
    private static partial int GetEventParameter(nint nativeEvent, uint name, uint desiredType, nint actualType,
        uint bufferSize, nint actualSize, out HotkeyId identifier);
}
