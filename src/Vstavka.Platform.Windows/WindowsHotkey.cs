using System.Globalization;
using Vstavka.Core;

namespace Vstavka.Platform.Windows;

internal sealed class WindowsHotkey(IWindowsMessages messages) : IGlobalHotkey
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    internal static IReadOnlyList<string> Gestures { get; } = Array.AsReadOnly(new[]
    {
        AppSettings.DefaultHotkey, "Ctrl+Alt+V", "Alt+V"
    });

    private SynchronizationContext? context;
    private uint modifiers;
    private uint virtualKey;
    private int hotkeyId;
    private int registrationGeneration;
    private bool disposed;

    public event Action? Pressed;

    public bool TryRegister(string gesture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!HotkeyGesture.TryParse(gesture, out var shortcut))
            return false;

        var nextModifiers = (shortcut.Modifiers.HasFlag(HotkeyModifiers.Alt) ? ModAlt : 0)
            | (shortcut.Modifiers.HasFlag(HotkeyModifiers.Ctrl) ? ModControl : 0)
            | (shortcut.Modifiers.HasFlag(HotkeyModifiers.Shift) ? ModShift : 0)
            | (shortcut.Modifiers.HasFlag(HotkeyModifiers.Meta) ? ModWin : 0);
        var nextKey = shortcut.Key switch
        {
            "Space" => 0x20u,
            { Length: 1 } key => key[0],
            var key => 0x70u + uint.Parse(key.AsSpan(1), CultureInfo.InvariantCulture) - 1
        };
        if (nextModifiers != modifiers || nextKey != virtualKey)
        {
            var nextId = hotkeyId == 1 ? 2 : 1;
            if (!messages.RegisterHotkey(nextId, nextModifiers | ModNoRepeat, nextKey))
                return false;
            if (hotkeyId == 0)
                messages.HotkeyPressed += OnHotkey;
            else if (!messages.UnregisterHotkey(hotkeyId))
            {
                messages.UnregisterHotkey(nextId);
                return false;
            }
            hotkeyId = nextId;
            modifiers = nextModifiers;
            virtualKey = nextKey;
        }
        context = SynchronizationContext.Current;
        registrationGeneration++;
        return true;
    }

    private void OnHotkey(int id, uint pressedModifiers, uint pressedKey)
    {
        if (id != hotkeyId || pressedModifiers != modifiers || pressedKey != virtualKey || disposed)
            return;
        var registrationContext = context;
        var generation = registrationGeneration;
        if (registrationContext is null)
            ThreadPool.QueueUserWorkItem(_ => Publish());
        else
            registrationContext.Post(_ => Publish(), null);

        void Publish()
        {
            if (!disposed && generation == registrationGeneration)
                Pressed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        if (hotkeyId != 0)
        {
            messages.UnregisterHotkey(hotkeyId);
            messages.HotkeyPressed -= OnHotkey;
            hotkeyId = 0;
        }
        disposed = true;
    }
}
