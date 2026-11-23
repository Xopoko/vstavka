using Vstavka.Core;

namespace Vstavka.Platform.Windows;

internal sealed class WindowsHotkey(IWindowsMessages messages) : IGlobalHotkey
{
    internal static IReadOnlyList<string> Gestures { get; } = Array.AsReadOnly(new[]
    {
        AppSettings.DefaultHotkey, "Ctrl+Alt+V", "Alt+V"
    });

    private SynchronizationContext? context;
    private uint modifiers;
    private int hotkeyId;
    private int registrationGeneration;
    private bool disposed;

    public event Action? Pressed;

    public bool TryRegister(string gesture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var canonicalGesture = Gestures.FirstOrDefault(value =>
            string.Equals(value, gesture?.Trim(), StringComparison.OrdinalIgnoreCase));
        var nextModifiers = canonicalGesture switch
        {
            AppSettings.DefaultHotkey => 0x0006u,
            "Ctrl+Alt+V" => 0x0003u,
            "Alt+V" => 0x0001u,
            _ => 0u
        };
        if (nextModifiers == 0)
            return false;
        if (nextModifiers != modifiers)
        {
            var nextId = hotkeyId == 1 ? 2 : 1;
            if (!messages.RegisterHotkey(nextId, nextModifiers | 0x4000, 0x56)) // MOD_NOREPEAT, V
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
        }
        context = SynchronizationContext.Current;
        registrationGeneration++;
        return true;
    }

    private void OnHotkey(int id, uint pressedModifiers)
    {
        if (id != hotkeyId || pressedModifiers != modifiers || disposed)
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
