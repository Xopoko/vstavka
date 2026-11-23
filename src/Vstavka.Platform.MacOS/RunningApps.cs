namespace Vstavka.Platform.MacOS;

internal static class RunningApps
{
    private const int MaxSourceNameLength = 1024; // HistoryStore's source name limit.

    internal static nint Frontmost => ObjC.Get(ObjC.Get(ObjC.Class("NSWorkspace"), "sharedWorkspace"), "frontmostApplication");
    internal static nint CurrentApp => ObjC.Get(ObjC.Class("NSApplication"), "sharedApplication");
    internal static bool IsCurrentProcess(nint app) => ProcessId(app) == Environment.ProcessId;
    internal static int ProcessId(nint app) => ObjC.Int32(app, ObjC.Sel("processIdentifier"));

    internal static FrontmostSource GetSource()
    {
        var app = Frontmost;
        return new FrontmostSource(IsCurrentProcess(app), app == 0 ? null : () => Describe(app));
    }

    internal static SourceApp? Describe(nint app)
    {
        var name = ObjC.ReadString(ObjC.Get(app, "localizedName"), MaxSourceNameLength);
        var id = ObjC.ReadString(ObjC.Get(app, "bundleIdentifier"), MaxSourceNameLength);
        return name is null && id is null
            ? null
            : new SourceApp(name ?? id!, id, MacImages.RenderIcon(ObjC.Get(app, "icon")));
    }
}
