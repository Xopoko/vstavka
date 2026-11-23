using System.Security.Cryptography;
using Vstavka.Core;

namespace Vstavka.Platform.MacOS;

public sealed class MacPlatformServices : IPlatformServices
{
    private readonly NativePasteboard board = new();
    private readonly MacPasteService paste = new();
    private readonly Lazy<IHistoryProtector?> historyProtector = new(CreateHistoryProtector);

    public static bool IsSupported => OperatingSystem.IsMacOS();

    public MacPlatformServices()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("The Vstavka macOS adapter requires macOS.");

        var poll = new ClipboardPoll(board, RunningApps.GetSource);
        Clipboard = new MacClipboardMonitor(poll);
        ClipboardWriter = new MacClipboardWriter(board, poll);
        Hotkey = new MacGlobalHotkey((gesture, callback) => CarbonHotkey.TryRegister(gesture, callback, out _, out _));
    }

    public string Name => "macOS";
    public string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Vstavka");
    public string DefaultGesture => MacHotkeyGesture.Default;
    public IReadOnlyList<string> SupportedGestures => MacHotkeyGesture.Supported;
    public IClipboardMonitor Clipboard { get; }
    public IClipboardWriter ClipboardWriter { get; }
    public IGlobalHotkey Hotkey { get; }
    public IPasteService Paste => paste;
    public IPanelWindow PanelWindow { get; } = new MacPanelWindow();
    public IHistoryProtector? HistoryProtector => historyProtector.Value;

    public PlatformCapabilities Capabilities
    {
        get
        {
            var capabilities = PlatformCapabilities.GlobalHotkey | PlatformCapabilities.SourceIcons | PlatformCapabilities.PanelPlacement;
            if (ClipboardCapturePolicy.CanReadInBackground(GetPasteboardAccess()))
                capabilities |= PlatformCapabilities.WatchClipboard;
            if (Paste.Permission == PermissionState.Granted)
                capabilities |= PlatformCapabilities.AutoPaste;
            if (HistoryProtector is not null)
                capabilities |= PlatformCapabilities.PersistentHistory;
            return capabilities;
        }
    }

    public IReadOnlyList<PlatformNotice> Notices
    {
        get
        {
            var notices = new List<PlatformNotice>();
            var access = GetPasteboardAccess();
            if (access == PasteboardAccess.AlwaysDeny)
                notices.Add(new("pasteboard-access-denied", "macOS denied clipboard access. Set Vstavka to Always Allow in System Settings to capture clipboard history.", OpenPasteboardSettings));
            else if (!ClipboardCapturePolicy.CanReadInBackground(access))
                notices.Add(new("pasteboard-access-approval", "Clipboard history needs Always Allow for Vstavka in System Settings. Background capture waits for approval.", NativePasteboard.RequestAccess));
            if (Paste.Permission == PermissionState.Missing)
                notices.Add(new("accessibility-permission", "Allow Vstavka in Accessibility to paste into other apps. You can still paste manually.", Paste.RequestPermission));
            if (HistoryProtector is null)
                notices.Add(new("keychain-unavailable", "The history key is unavailable in macOS Keychain. History is kept in memory."));
            return notices;
        }
    }

    private PasteboardAccess GetPasteboardAccess()
    {
        using var pool = new AutoreleasePool();
        return board.Access;
    }

    private static void OpenPasteboardSettings()
    {
        using var pool = new AutoreleasePool();
        var workspace = ObjC.Get(ObjC.Class("NSWorkspace"), "sharedWorkspace");
        var url = ObjC.Get(ObjC.Class("NSURL"), "URLWithString:",
            ObjC.String("x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Pasteboard"));
        ObjC.Bool(workspace, ObjC.Sel("openURL:"), url);
    }

    private static IHistoryProtector? CreateHistoryProtector()
    {
        try
        {
            var store = new KeychainHistoryKeyStore();
            store.EnsureKey();
            return new AesGcmHistoryProtector(store);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Clipboard.Dispose();
        Hotkey.Dispose();
        paste.Dispose();
    }
}
