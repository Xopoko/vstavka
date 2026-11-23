namespace Vstavka.Platform.MacOS;

internal sealed class MacPermissions(
    Func<PasteboardAccess> getPasteboardAccess,
    Func<bool> isAccessibilityTrusted,
    Action requestPasteboardAccess,
    Action promptAccessibility,
    Action<string> openSettings)
{
    private const string ClipboardSettings = "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Pasteboard";
    private const string AccessibilitySettings = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    private int accessibilityPromptRequested;

    internal static MacPermissions System { get; } = new(
        NativePasteboard.GetAccess, Accessibility.IsTrusted, NativePasteboard.RequestAccess, Accessibility.Prompt, OpenSettings);

    internal bool CanReadClipboard => ClipboardCapturePolicy.CanReadInBackground(getPasteboardAccess());
    internal PermissionState AccessibilityState => isAccessibilityTrusted() ? PermissionState.Granted : PermissionState.Missing;

    internal IReadOnlyList<PlatformPermission> GetPermissions() =>
    [
        GetClipboardPermission(getPasteboardAccess()),
        GetAccessibilityPermission(),
    ];

    internal List<PlatformNotice> GetNotices()
    {
        var notices = new List<PlatformNotice>();
        var access = getPasteboardAccess();
        var clipboard = GetClipboardPermission(access);
        if (clipboard.State == PermissionState.Missing)
        {
            notices.Add(access == PasteboardAccess.AlwaysDeny
                ? new("pasteboard-access-denied", "macOS denied clipboard access. Set Vstavka to Always Allow in System Settings to capture clipboard history.", clipboard.Request)
                : new("pasteboard-access-approval", "Clipboard history needs Always Allow for Vstavka in System Settings. Background capture waits for approval.", clipboard.Request));
        }
        var accessibility = GetAccessibilityPermission();
        if (accessibility.State == PermissionState.Missing)
            notices.Add(new("accessibility-permission", "Allow Vstavka in Accessibility to paste into other apps. You can still paste manually.", accessibility.Request));
        return notices;
    }

    internal void RequestAccessibility()
    {
        if (isAccessibilityTrusted())
            return;
        if (Interlocked.Exchange(ref accessibilityPromptRequested, 1) == 0)
            promptAccessibility();
        else
            openSettings(AccessibilitySettings);
    }

    private PlatformPermission GetClipboardPermission(PasteboardAccess access) =>
        ClipboardCapturePolicy.CanReadInBackground(access)
            ? new("clipboard-access", PermissionRequirement.Required, PermissionState.Granted)
            : new("clipboard-access", PermissionRequirement.Required, PermissionState.Missing, RequestClipboard);

    private PlatformPermission GetAccessibilityPermission()
    {
        var state = AccessibilityState;
        return new("accessibility", PermissionRequirement.Optional, state,
            state == PermissionState.Missing ? RequestAccessibility : null);
    }

    private void RequestClipboard()
    {
        var access = getPasteboardAccess();
        if (access is PasteboardAccess.Default or PasteboardAccess.Ask)
            requestPasteboardAccess();
        else if (!ClipboardCapturePolicy.CanReadInBackground(access))
            openSettings(ClipboardSettings);
    }

    private static void OpenSettings(string address)
    {
        using var pool = new AutoreleasePool();
        var workspace = ObjC.Get(ObjC.Class("NSWorkspace"), "sharedWorkspace");
        var url = ObjC.Get(ObjC.Class("NSURL"), "URLWithString:", ObjC.String(address));
        ObjC.Bool(workspace, ObjC.Sel("openURL:"), url);
    }
}
