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
}

/// <summary>
/// <see cref="RestartRequired"/>: granted or installed, but it takes effect only after the user signs out and back in
/// (for example a new GNOME Shell extension). Its <see cref="PlatformPermission.Request"/>, when set, starts signing out.
/// </summary>
public enum PermissionState { NotNeeded, Granted, Missing, RestartRequired }

public enum PermissionRequirement { Required, Optional }

/// <summary>A system permission or helper Vstavka needs on this platform, for onboarding and settings.</summary>
/// <param name="Code">Stable identifier: <c>clipboard-access</c> and <c>accessibility</c> on macOS, <c>gnome-helper</c> on GNOME.</param>
/// <param name="Requirement">Required for capture, or optional (Vstavka still works, with a manual step).</param>
/// <param name="Request">Starts the system flow on a user's click: a permission prompt, a settings page or a helper
/// install. Null when the app cannot help, for example when the permission is already granted.</param>
public sealed record PlatformPermission(string Code, PermissionRequirement Requirement, PermissionState State, Action? Request = null);

/// <summary>Starting Vstavka in the background when the user signs in.</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    /// <returns>False when the system refused; the previous state stays.</returns>
    bool TrySetEnabled(bool enabled);
}

/// <summary>Used until a platform implements start at login.</summary>
public sealed class UnsupportedStartupRegistration : IStartupRegistration
{
    public static UnsupportedStartupRegistration Instance { get; } = new();
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public bool TrySetEnabled(bool enabled) => false;
}

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

    /// <param name="gesture">Any usable <see cref="HotkeyGesture"/> in its text form, for example <c>Ctrl+Shift+V</c> or <c>Shift+Meta+V</c>.</param>
    /// <returns>False when the shortcut is taken or unsupported; the previous registration then stays active.</returns>
    bool TryRegister(string gesture);
}

public enum PasteResult { ShortcutSent, Manual, ClipboardChanged }

/// <summary>Returns focus to the app the user came from and sends the platform paste shortcut.</summary>
public interface IPasteService
{
    /// <summary>Called right before the panel opens: remember the app or window that should receive the paste.</summary>
    void CaptureTarget();

    /// <summary>Called after the panel hides. The clipboard already holds the chosen item.</summary>
    Task<PasteResult> PasteAsync();
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

public interface IStoreReview
{
    string? AppVersion { get; }

    /// <summary>Calls the system review API on the UI thread. The result reports whether the call was made.</summary>
    bool TryRequest();
}

public interface IClipboardSounds : IDisposable
{
    event Action? PasteShortcutPressed;

    void WatchPasteShortcuts(bool enabled);

    void Play(bool paste, double volume);
}

public interface IPlatformServices : IDisposable
{
    string Name { get; }

    PlatformCapabilities Capabilities { get; }

    IReadOnlyList<PlatformNotice> Notices { get; }

    /// <summary>Folder for <c>history.vstavka</c>.</summary>
    string DataDirectory { get; }

    /// <summary>The platform's default shortcut in <see cref="HotkeyGesture"/> text form.</summary>
    string DefaultGesture { get; }

    /// <summary>Permissions and helpers, evaluated on every read. The UI re-reads them while a permission screen is visible.</summary>
    IReadOnlyList<PlatformPermission> Permissions => [];

    IStartupRegistration Startup => UnsupportedStartupRegistration.Instance;

    IStoreReview? StoreReview => null;

    IClipboardSounds? ClipboardSounds => null;

    IClipboardMonitor Clipboard { get; }

    IClipboardWriter ClipboardWriter { get; }

    IGlobalHotkey Hotkey { get; }

    IPasteService Paste { get; }

    IPanelWindow PanelWindow { get; }

    /// <summary>Null when the system credential store is unavailable; history then stays in memory only.</summary>
    IHistoryProtector? HistoryProtector { get; }
}
