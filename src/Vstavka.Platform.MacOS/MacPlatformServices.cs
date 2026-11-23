using System.Security.Cryptography;
using Vstavka.Core;

namespace Vstavka.Platform.MacOS;

public sealed class MacPlatformServices : IPlatformServices
{
    private readonly NativePasteboard board = new();
    private readonly MacPasteService paste = new();
    private readonly Lazy<IHistoryProtector?> historyProtector = new(CreateHistoryProtector);

    public static bool IsSupported => OperatingSystem.IsMacOS();
    public bool IsAppStoreBuild { get; } =
#if VSTAVKA_MAC_APP_STORE
        true;
#else
        false;
#endif

    public MacPlatformServices()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("The Vstavka macOS adapter requires macOS.");

        var poll = new ClipboardPoll(board, RunningApps.GetSource);
        Clipboard = new MacClipboardMonitor(poll);
        ClipboardWriter = new MacClipboardWriter(board, poll);
        Hotkey = new MacGlobalHotkey(CarbonHotkey.TryRegister);
    }

    public string Name => "macOS";
    public string DataDirectory => Path.Combine(GetApplicationSupportDirectory(), "Vstavka");
    public string DefaultGesture => MacGlobalHotkey.DefaultGesture;
    public IReadOnlyList<string> SupportedGestures => MacGlobalHotkey.SupportedGestures;
    public IReadOnlyList<PlatformPermission> Permissions => MacPermissions.System.GetPermissions();
    public IStartupRegistration Startup => MacStartupRegistration.System;
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
            if (MacPermissions.System.CanReadClipboard)
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
            var notices = MacPermissions.System.GetNotices();
            if (HistoryProtector is null)
                notices.Add(new("keychain-unavailable", "The history key is unavailable in macOS Keychain. History is kept in memory."));
            return notices;
        }
    }

    private static string GetApplicationSupportDirectory()
    {
        const int applicationSupportDirectory = 14;
        const int userDomain = 1;
        using var pool = new AutoreleasePool();
        var manager = ObjC.Get(ObjC.Class("NSFileManager"), "defaultManager");
        var urls = ObjC.Send(manager, ObjC.Sel("URLsForDirectory:inDomains:"), applicationSupportDirectory, userDomain);
        return ObjC.ReadString(ObjC.Get(ObjC.Get(urls, "firstObject"), "path"))
            ?? throw new InvalidOperationException("macOS could not locate the Application Support directory.");
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
