using Vstavka.Core;

namespace Vstavka.Platform;

/// <summary>The app that put content on the clipboard, when the platform can tell.</summary>
/// <param name="Name">Display or process name; matched against the excluded apps list.</param>
/// <param name="Id">Stable identifier when available: bundle ID, desktop file ID or executable path.</param>
/// <param name="IconPng">A PNG of at most 32 × 32 pixels and 64 KiB, or null.</param>
public sealed record SourceApp(string Name, string? Id = null, byte[]? IconPng = null);

/// <summary>One new clipboard entry. <see cref="ClipItem.SourceApp"/> already holds <see cref="SourceApp.Name"/>.</summary>
public sealed record ClipboardCapture(ClipItem Item, SourceApp? Source);

[Flags]
public enum PlatformCapabilities
{
    None = 0,
    WatchClipboard = 1 << 0,
    GlobalHotkey = 1 << 1,
    AutoPaste = 1 << 2,
    SourceIcons = 1 << 3,
    PersistentHistory = 1 << 4,
    PanelPlacement = 1 << 5,
}

public enum PermissionState { NotNeeded, Granted, Missing }

/// <summary>Something the user should know or do, such as granting a permission or installing a helper.</summary>
/// <param name="Code">Stable identifier, for example <c>accessibility-permission</c> or <c>gnome-extension-missing</c>.</param>
/// <param name="Message">Plain-language explanation in English; the UI localizes known codes.</param>
/// <param name="Resolve">When set, the UI shows a button that calls this action. The user's click
/// consents to the action, such as requesting a permission or opening settings.</param>
public sealed record PlatformNotice(string Code, string Message, Action? Resolve = null);

/// <summary>Watches the system clipboard and reports new entries.</summary>
/// <remarks>
/// Events are raised on the <see cref="SynchronizationContext"/> that was current when <see cref="Start"/> was called.
/// Implementations skip content that apps mark as private (password managers, transient or concealed types).
/// </remarks>
public interface IClipboardMonitor : IDisposable
{
    event Action<ClipboardCapture>? Captured;

    void Start();

    /// <summary>The next change comes from Vstavka itself and must not become a new entry.</summary>
    void SuppressNextChange();
}

public interface IClipboardWriter
{
    /// <summary>Puts an item on the system clipboard. Plain text drops formatting; file items write their paths as text.</summary>
    void Write(ClipItem item, bool plainText);
}

public interface IGlobalHotkey : IDisposable
{
    /// <summary>Raised on the synchronization context that was current when <see cref="TryRegister"/> was called.</summary>
    event Action? Pressed;

    /// <param name="gesture">One of <see cref="IPlatformServices.SupportedGestures"/>.</param>
    /// <returns>False when the shortcut is taken or unsupported; the previous registration then stays active.</returns>
    bool TryRegister(string gesture);
}

/// <summary>Returns focus to the app the user came from and sends the platform paste shortcut.</summary>
public interface IPasteService
{
    PermissionState Permission { get; }

    /// <summary>Called right before the panel opens: remember the app or window that should receive the paste.</summary>
    void CaptureTarget();

    /// <summary>Called after the panel hides. The clipboard already holds the chosen item.</summary>
    /// <returns>False when the platform could not paste; the item then stays on the clipboard for a manual paste.</returns>
    Task<bool> PasteAsync();

    /// <summary>Asks the system for the permission that automatic paste needs, for example macOS Accessibility.</summary>
    void RequestPermission();
}

/// <summary>Native tweaks for the panel window: level, spaces, placement, taskbar visibility.</summary>
public interface IPanelWindow
{
    /// <param name="handle">Native handle of the Avalonia window.</param>
    /// <param name="descriptor">Avalonia handle descriptor: <c>HWND</c>, <c>NSWindow</c> or <c>XID</c>.</param>
    void Attach(nint handle, string descriptor);

    void Showing();

    void Hidden();
}

public interface IPlatformServices : IDisposable
{
    string Name { get; }

    PlatformCapabilities Capabilities { get; }

    IReadOnlyList<PlatformNotice> Notices { get; }

    /// <summary>Folder for <c>history.vstavka</c>.</summary>
    string DataDirectory { get; }

    string DefaultGesture { get; }

    IReadOnlyList<string> SupportedGestures { get; }

    IClipboardMonitor Clipboard { get; }

    IClipboardWriter ClipboardWriter { get; }

    IGlobalHotkey Hotkey { get; }

    IPasteService Paste { get; }

    IPanelWindow PanelWindow { get; }

    /// <summary>Null when the system credential store is unavailable; history then stays in memory only.</summary>
    IHistoryProtector? HistoryProtector { get; }
}
