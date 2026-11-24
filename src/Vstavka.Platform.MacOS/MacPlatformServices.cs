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
    public IReadOnlyList<PlatformPermission> Permissions => MacPermissions.System.GetPermissions();
    public IStartupRegistration Startup => MacStartupRegistration.System;
#if VSTAVKA_MAC_APP_STORE
    public IStoreReview StoreReview { get; } = new MacStoreReview();
#endif
    public IClipboardMonitor Clipboard { get; }
    public IClipboardWriter ClipboardWriter { get; }
    public IGlobalHotkey Hotkey { get; }
    public IPasteService Paste => paste;
    public IPanelWindow PanelWindow { get; } = new MacPanelWindow();
    public IHistoryProtector? HistoryProtector => historyProtector.Value;

    public PlatformCapabilities Capabilities => MacPermissions.System.CanReadClipboard
        ? PlatformCapabilities.GlobalHotkey | PlatformCapabilities.WatchClipboard
        : PlatformCapabilities.GlobalHotkey;

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
            var key = store.TryLoadKey();
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
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
