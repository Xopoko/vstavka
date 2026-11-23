using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal readonly record struct MacHotkeyGesture(uint Modifiers)
{
    private const uint Command = 1 << 8;
    private const uint Shift = 1 << 9;
    private const uint Option = 1 << 11;
    private const uint Control = 1 << 12;

    internal const uint KeyCodeV = 9; // Carbon kVK_ANSI_V, the physical key position.
    internal const string Default = "Cmd+Shift+V";
    private static readonly Dictionary<string, MacHotkeyGesture> Gestures = new(StringComparer.OrdinalIgnoreCase)
    {
        [Default] = new(Command | Shift),
        ["Ctrl+Cmd+V"] = new(Command | Control),
        ["Option+Cmd+V"] = new(Command | Option)
    };

    internal static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(Gestures.Keys.ToArray());

    internal static bool TryParse(string gesture, out MacHotkeyGesture result) => Gestures.TryGetValue(gesture.Trim(), out result);
}

internal sealed class MacGlobalHotkey(Func<MacHotkeyGesture, Action, IDisposable?> register) : IGlobalHotkey
{
    private Binding? current;
    private bool disposed;

    public event Action? Pressed;

    public bool TryRegister(string gesture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!MacHotkeyGesture.TryParse(gesture, out var selection))
            return false;
        var context = SynchronizationContext.Current;
        if (current is { } existing && existing.Gesture == selection)
        {
            existing.Context = context;
            return true;
        }

        var binding = new Binding(selection, context);
        var registration = register(selection, () => Dispatch(binding));
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

    private sealed class Binding(MacHotkeyGesture gesture, SynchronizationContext? context)
    {
        internal MacHotkeyGesture Gesture { get; } = gesture;
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

    private static readonly ConcurrentDictionary<uint, Action> Handlers = new();
    private static int nextId;

    private readonly uint id;
    private nint handler;
    private nint registration;
    internal int? UnregisterStatus { get; private set; }

    private CarbonHotkey(uint id) => this.id = id;

    internal static unsafe CarbonHotkey? TryRegister(MacHotkeyGesture gesture, Action pressed, out string operation, out int status)
    {
        var id = unchecked((uint)Interlocked.Increment(ref nextId));
        var binding = new CarbonHotkey(id);
        var target = GetApplicationEventTarget();
        var type = new EventType(KeyboardEventClass, HotkeyPressed);
        operation = "InstallEventHandler";
        status = InstallEventHandler(target, &HandleEvent, 1, &type, (nint)id, out binding.handler);
        if (status != 0)
            return null;

        operation = "RegisterEventHotKey";
        status = RegisterEventHotKey(MacHotkeyGesture.KeyCodeV, gesture.Modifiers, new HotkeyId(Signature, id), target, Exclusive,
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
            UnregisterStatus = UnregisterEventHotKey(Interlocked.Exchange(ref registration, 0));
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
