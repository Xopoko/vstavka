using Vstavka.Core;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal static class PlatformFactory
{
    public static IPlatformServices Create(string[] args)
    {
        var demo = new DemoPlatformServices();
        return IsDemo(args) ? demo : new AdapterUnavailable(demo);
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
