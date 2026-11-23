using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal sealed class MacPasteService : IPasteService, IDisposable
{
    private const int IgnoreOtherApps = 1 << 1;
    private nint target;
    private int targetGeneration;
    private bool disposed;

    public PermissionState Permission => MacPermissions.System.AccessibilityState;

    public void CaptureTarget()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var pool = new AutoreleasePool();
        var app = RunningApps.Frontmost;
        if (app != 0 && !RunningApps.IsCurrentProcess(app))
            ObjC.Retain(app);
        else
            app = 0;
        ObjC.Release(target);
        target = app;
        targetGeneration++;
    }

    public async Task<bool> PasteAsync()
    {
        if (disposed || target == 0 || Permission != PermissionState.Granted)
            return false;

        var app = target;
        target = 0;
        var generation = ++targetGeneration;
        try
        {
            using (var pool = new AutoreleasePool())
            {
                if (ObjC.Is(app, "isTerminated"))
                    return false;
                Activate(app);
            }

            for (var i = 0; i < 10; i++)
            {
                // Resume on the UI context so AppKit can process the activation between checks.
                await Task.Delay(50);
                if (generation != targetGeneration)
                    return false;
                using var pool = new AutoreleasePool();
                if (ObjC.Is(app, "isTerminated"))
                    return false;
                if (RunningApps.ProcessId(app) == RunningApps.ProcessId(RunningApps.Frontmost))
                    return Permission == PermissionState.Granted && Accessibility.PostPaste();
            }
            return false;
        }
        finally
        {
            ObjC.Release(app);
        }
    }

    private static void Activate(nint app)
    {
        var current = RunningApps.CurrentApp;
        if (ObjC.Responds(current, "yieldActivationToApplication:"))
        {
            ObjC.Void(current, ObjC.Sel("yieldActivationToApplication:"), app);
            ObjC.Bool(app, ObjC.Sel("activateWithOptions:"), 0);
        }
        else
        {
            ObjC.Bool(app, ObjC.Sel("activateWithOptions:"), IgnoreOtherApps);
        }
    }

    public void RequestPermission()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        MacPermissions.System.RequestAccessibility();
    }

    public void Dispose()
    {
        disposed = true;
        targetGeneration++;
        ObjC.Release(target);
        target = 0;
    }
}

internal static partial class Accessibility
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const ulong CommandMask = 1UL << 20; // kCGEventFlagMaskCommand
    private const uint HidEventTap = 0;

    internal static bool IsTrusted() => AXIsProcessTrusted() != 0;

    internal static void Prompt()
    {
        var framework = NativeLibrary.Load(ApplicationServices);
        try
        {
            var option = CoreFoundation.ReadConstant(framework, "kAXTrustedCheckOptionPrompt");
            var options = CoreFoundation.CreateDictionary();
            try
            {
                CoreFoundation.SetValue(options, option, CoreFoundation.True);
                AXIsProcessTrustedWithOptions(options);
            }
            finally
            {
                CoreFoundation.Release(options);
            }
        }
        finally
        {
            NativeLibrary.Free(framework);
        }
    }

    internal static bool PostPaste()
    {
        var down = CGEventCreateKeyboardEvent(0, CarbonHotkey.KeyCodeV, 1);
        var up = CGEventCreateKeyboardEvent(0, CarbonHotkey.KeyCodeV, 0);
        try
        {
            if (down == 0 || up == 0)
                return false;
            CGEventSetFlags(down, CommandMask);
            CGEventSetFlags(up, CommandMask);
            CGEventPost(HidEventTap, down);
            CGEventPost(HidEventTap, up);
            return true;
        }
        finally
        {
            CoreFoundation.Release(down);
            CoreFoundation.Release(up);
        }
    }

    [LibraryImport(ApplicationServices)]
    private static partial byte AXIsProcessTrusted();
    [LibraryImport(ApplicationServices)]
    private static partial byte AXIsProcessTrustedWithOptions(nint options);
    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreateKeyboardEvent(nint source, ushort keyCode, byte keyDown);
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetFlags(nint nativeEvent, ulong flags);
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventPost(uint tap, nint nativeEvent);
}
