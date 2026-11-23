using System.Security.Principal;
using Vstavka.Core;

namespace Vstavka.Platform.Windows;

public sealed class WindowsPlatformServices : IPlatformServices
{
    private readonly WindowsMessageWindow window;
    private readonly WindowsClipboard clipboard;
    private readonly WindowsHotkey hotkey;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Create the adapter. Start, register, write and dispose on the Avalonia UI thread.</summary>
    /// <param name="settings">Supplies current app exclusions. Defaults to the built-in exclusions.</param>
    public WindowsPlatformServices(Func<AppSettings>? settings = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows platform adapter requires Windows.");

        DataDirectory = PreviewDataDirectory(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        window = new WindowsMessageWindow();
        var backend = new Win32Clipboard(window);
        var defaultSettings = new AppSettings();
        clipboard = new WindowsClipboard(window, backend, new WindowsClipboardImages(),
            new WindowsSourceIcons(), settings ?? (() => defaultSettings));
        hotkey = new WindowsHotkey(window);
        Paste = new WindowsPaste(new Win32Paste(), () => backend.Sequence, () => clipboard.LastWrittenSequence);
        PanelWindow = new WindowsPanelWindow();
        HistoryProtector = new WindowsHistoryProtector();
        Startup = new WindowsStartupRegistration(new WindowsRunKey(), Environment.ProcessPath);
    }

    public string Name => "Windows";
    public PlatformCapabilities Capabilities => PlatformCapabilities.WatchClipboard | PlatformCapabilities.GlobalHotkey |
        PlatformCapabilities.AutoPaste | PlatformCapabilities.SourceIcons | PlatformCapabilities.PersistentHistory |
        PlatformCapabilities.PanelPlacement;
    public IReadOnlyList<PlatformNotice> Notices { get; } = [];
    public IReadOnlyList<PlatformPermission> Permissions { get; } = [];
    public IStartupRegistration Startup { get; }
    public string DataDirectory { get; }
    public string DefaultGesture => AppSettings.DefaultHotkey;
    public IReadOnlyList<string> SupportedGestures => WindowsHotkey.Gestures;
    public IClipboardMonitor Clipboard => clipboard;
    public IClipboardWriter ClipboardWriter => clipboard;
    public IGlobalHotkey Hotkey => hotkey;
    public IPasteService Paste { get; }
    public IPanelWindow PanelWindow { get; }
    public IHistoryProtector HistoryProtector { get; }

    /// <summary>Transient capture failures, delivered on the clipboard monitor's synchronization context.</summary>
    public event Action<PlatformNotice>? ClipboardNotice
    {
        add => clipboard.Notice += value;
        remove => clipboard.Notice -= value;
    }

    /// <summary>Use this name for the preview mutex and append ".Activate" for its activation event.</summary>
    public static string GetSingleInstanceName()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows instance name requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        return PreviewInstanceName(identity.User?.Value ??
            throw new InvalidOperationException("The current Windows user has no SID."));
    }

    internal static string PreviewDataDirectory(string localAppData)
    {
        if (!Path.IsPathFullyQualified(localAppData))
            throw new InvalidOperationException("Windows did not return an absolute LocalAppData path.");
        return Path.Combine(localAppData, "Vstavka.Preview");
    }

    internal static string PreviewInstanceName(string userSid) => $@"Local\Vstavka.Preview.{userSid}";

    public void Dispose()
    {
        clipboard.Dispose();
        hotkey.Dispose();
        window.Dispose();
    }
}
