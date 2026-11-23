using Vstavka.Core;

namespace Vstavka.Platform.Linux;

internal interface ILinuxDesktop : IDisposable
{
    PlatformCapabilities Capabilities { get; }
    event Action<LinuxClipboardData>? ClipboardChanged;
    event Action? HotkeyPressed;
    void StartWatching();
    void StopWatching();
    bool Write(string mime, byte[] data);
    bool RegisterHotkey(LinuxGesture gesture);
    void ReleaseHotkey();
    void CaptureTarget();
    Task<bool> PasteAsync();
    void PreparePanel();
}

internal sealed record LinuxClipboardData(
    string Mime, byte[] Data, string? DesktopId = null, string? Name = null, string? IconPath = null,
    IReadOnlyList<string>? Formats = null, string? PasswordHint = null, bool OwnWrite = false);

internal sealed record LinuxGesture(string Name, string Accelerator, uint XModifiers)
{
    private static readonly LinuxGesture[] Gestures =
    [
        new("Ctrl+Shift+V", "<Control><Shift>v", (1 << 2) | (1 << 0)),
        new("Ctrl+Alt+V", "<Control><Alt>v", (1 << 2) | (1 << 3)),
        new("Alt+V", "<Alt>v", 1 << 3),
    ];

    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(Gestures.Select(gesture => gesture.Name).ToArray());
    public static LinuxGesture? Find(string name) => Array.Find(Gestures,
        gesture => string.Equals(gesture.Name, name, StringComparison.OrdinalIgnoreCase));
}

internal enum LinuxSession { Unknown, X11, Wayland }
internal enum GnomeAccess { Unavailable, Denied, Busy }
internal readonly record struct GnomeConnection(ILinuxDesktop? Desktop, GnomeAccess Access = GnomeAccess.Unavailable);

internal static class LinuxDesktop
{
    public static LinuxSession DetectSession(Func<string, string?> environment)
    {
        var type = environment("XDG_SESSION_TYPE")?.ToLowerInvariant();
        return type switch
        {
            "x11" => LinuxSession.X11,
            "wayland" => LinuxSession.Wayland,
            _ when !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY")) => LinuxSession.Wayland,
            _ when !string.IsNullOrEmpty(environment("DISPLAY")) => LinuxSession.X11,
            _ => LinuxSession.Unknown,
        };
    }

    public static ILinuxDesktop? Connect(LinuxSession session, GnomeConnection shell, Func<ILinuxDesktop?> connectX11) =>
        shell.Desktop ?? (session == LinuxSession.X11 ? connectX11() : null);
}
