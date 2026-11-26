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
    Task<PasteResult> PasteAsync();
    void PreparePanel();
}

internal sealed record LinuxClipboardData(
    string Mime, byte[] Data, string? DesktopId = null, string? Name = null, string? IconPath = null);

internal sealed record LinuxGesture(string Accelerator, nuint XKeySym, uint XModifiers)
{
    private static readonly (HotkeyModifiers Modifier, string Gnome, uint XMask)[] Modifiers =
    [
        (HotkeyModifiers.Ctrl, "<Control>", X11Native.ControlMask),
        (HotkeyModifiers.Alt, "<Alt>", X11Native.Mod1Mask),
        (HotkeyModifiers.Shift, "<Shift>", X11Native.ShiftMask),
        (HotkeyModifiers.Meta, "<Super>", X11Native.Mod4Mask),
    ];

    public static LinuxGesture? Parse(string text)
    {
        if (!HotkeyGesture.TryParse(text, out var gesture))
            return null;
        var accelerator = new System.Text.StringBuilder();
        uint xModifiers = 0;
        foreach (var (modifier, gnome, mask) in Modifiers)
        {
            if (!gesture.Modifiers.HasFlag(modifier))
                continue;
            accelerator.Append(gnome);
            xModifiers |= mask;
        }

        var key = gesture.Key;
        if (key.Length == 1)
            key = key.ToLowerInvariant();
        else if (key == "Space")
            key = "space";
        var keysym = key.Length == 1 ? (nuint)key[0]
            : key == "space" ? (nuint)' '
            : 0xffbdu + uint.Parse(key.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture); // XK_F1 is 0xffbe.
        return new(accelerator.Append(key).ToString(), keysym, xModifiers);
    }
}

internal enum LinuxSession { Unknown, X11, Wayland }
internal enum GnomeAccess { Unavailable, Denied, Busy, SessionUnavailable, Unknown }
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

    public static bool IsGnome(Func<string, string?> environment) =>
        (environment("XDG_CURRENT_DESKTOP") ?? environment("XDG_SESSION_DESKTOP") ?? environment("DESKTOP_SESSION") ?? "")
            .Split(':').Any(name => name.Equals("GNOME", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ubuntu", StringComparison.OrdinalIgnoreCase));
}
