using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal sealed partial class MacPanelWindow : IPanelWindow
{
    private const nint CanJoinAllSpaces = 1 << 0;
    private const nint FullScreenAuxiliary = 1 << 8;
    private const int StatusWindowLevelKey = 9;

    public void Attach(nint handle, string descriptor)
    {
        if (handle == 0 || descriptor != "NSWindow")
            return;
        using var pool = new AutoreleasePool();
        ObjC.Void(handle, ObjC.Sel("setLevel:"), CGWindowLevelForKey(StatusWindowLevelKey));
        // Show the one panel on the current Space, including another app's full-screen Space.
        ObjC.Void(handle, ObjC.Sel("setCollectionBehavior:"), CanJoinAllSpaces | FullScreenAuxiliary);
    }

    public void Showing()
    {
        using var pool = new AutoreleasePool();
        var app = RunningApps.CurrentApp;
        if (ObjC.Responds(app, "activate"))
            ObjC.Void(app, ObjC.Sel("activate"));
        else
            ObjC.Void(app, ObjC.Sel("activateIgnoringOtherApps:"), (byte)1);
    }

    public void Hidden() { }

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial int CGWindowLevelForKey(int key);
}
