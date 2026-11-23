using Vstavka.Core;
using Vstavka.Platform;
using Vstavka.Platform.MacOS;
using Vstavka.Platform.Windows;

namespace Vstavka.Desktop;

internal static class PlatformFactory
{
    public static IPlatformServices Create(string[] args, Func<AppSettings>? settings = null)
    {
        if (IsDemo(args))
            return new DemoPlatformServices();
        if (OperatingSystem.IsWindows())
            return new WindowsPlatformServices(settings);
        if (OperatingSystem.IsMacOS())
            return new MacPlatformServices();
        return new AdapterUnavailable(new DemoPlatformServices());
    }

    public static bool IsDemo(string[] args) => args.Contains("--demo", StringComparer.Ordinal);

    private sealed class AdapterUnavailable(DemoPlatformServices demo) : IPlatformServices
    {
        public string Name => demo.Name;
        public PlatformCapabilities Capabilities => demo.Capabilities;
        public IReadOnlyList<PlatformNotice> Notices { get; } =
            [new("adapter-not-wired", "Clipboard integration is unavailable in this build.")];
        public string DataDirectory => demo.DataDirectory;
        public string DefaultGesture => demo.DefaultGesture;
        public IReadOnlyList<string> SupportedGestures => demo.SupportedGestures;
        public IClipboardMonitor Clipboard => demo.Clipboard;
        public IClipboardWriter ClipboardWriter => demo.ClipboardWriter;
        public IGlobalHotkey Hotkey => demo.Hotkey;
        public IPasteService Paste => demo.Paste;
        public IPanelWindow PanelWindow => demo.PanelWindow;
        public IHistoryProtector? HistoryProtector => demo.HistoryProtector;
        public void Dispose() => demo.Dispose();
    }
}
