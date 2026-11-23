using Vstavka.Core;

namespace Vstavka.Platform;

/// <summary>
/// Runs the UI anywhere without touching the system clipboard, shortcuts or other apps.
/// Used by <c>--demo</c>, headless rendering and tests. History stays in memory.
/// </summary>
public sealed class DemoPlatformServices : IPlatformServices, IClipboardMonitor, IClipboardWriter, IGlobalHotkey, IPasteService, IPanelWindow
{
    public string Name => "Demo";
    public PlatformCapabilities Capabilities => PlatformCapabilities.None;
    public IReadOnlyList<PlatformNotice> Notices { get; } = [];
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "Vstavka.Demo");
    public string DefaultGesture => "Ctrl+Shift+V";
    public IReadOnlyList<string> SupportedGestures { get; } = ["Ctrl+Shift+V"];
    public IClipboardMonitor Clipboard => this;
    public IClipboardWriter ClipboardWriter => this;
    public IGlobalHotkey Hotkey => this;
    public IPasteService Paste => this;
    public IPanelWindow PanelWindow => this;
    public IHistoryProtector? HistoryProtector => null;
    public PermissionState Permission => PermissionState.NotNeeded;

    /// <summary>The last item the UI asked to write; demo mode never touches the real clipboard.</summary>
    public ClipItem? LastWritten { get; private set; }

    public int PasteRequests { get; private set; }

    event Action<ClipboardCapture>? IClipboardMonitor.Captured { add { } remove { } }
    event Action? IGlobalHotkey.Pressed { add { } remove { } }

    public void Start() { }
    public void SuppressNextChange() { }
    public void Write(ClipItem item, bool plainText) => LastWritten = item;
    public bool TryRegister(string gesture) => SupportedGestures.Contains(gesture);
    public void CaptureTarget() { }

    public Task<bool> PasteAsync()
    {
        PasteRequests++;
        return Task.FromResult(false);
    }

    public void RequestPermission() { }
    public void Attach(nint handle, string descriptor) { }
    public void Showing() { }
    public void Hidden() { }
    public void Dispose() { }
}
