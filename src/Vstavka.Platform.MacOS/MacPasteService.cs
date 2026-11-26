using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal interface IMacPaste
{
    nint CaptureTarget();
    void ReleaseTarget(nint app);
    bool TryActivate(nint app);
    bool IsTerminated(nint app);
    bool IsFrontmost(nint app);
    bool CanPostPaste { get; }
    bool PostPaste();
}

internal sealed class MacPasteService(IMacPaste native, Func<long> changeCount, Func<long?> writtenCount,
    Func<int, Task>? delay = null) : IPasteService, IDisposable
{
    private readonly Func<int, Task> wait = delay ?? (milliseconds => Task.Delay(milliseconds));
    private nint target;
    private int targetGeneration;
    private bool disposed;

    public void CaptureTarget()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var app = native.CaptureTarget();
        native.ReleaseTarget(target);
        target = app;
        targetGeneration++;
    }

    public async Task<PasteResult> PasteAsync()
    {
        var count = writtenCount();
        bool ClipboardUnchanged() => count.HasValue && writtenCount() == count && changeCount() == count;
        PasteResult Failure() => ClipboardUnchanged() ? PasteResult.Manual : PasteResult.ClipboardChanged;

        if (disposed || target == 0)
            return Failure();

        var app = target;
        target = 0;
        var generation = ++targetGeneration;
        try
        {
            if (!ClipboardUnchanged())
                return PasteResult.ClipboardChanged;
            if (!native.TryActivate(app) || !native.CanPostPaste)
                return Failure();
            for (var i = 0; i < 10; i++)
            {
                // Resume on the UI context so AppKit can process the activation between checks.
                await wait(50);
                if (generation != targetGeneration || native.IsTerminated(app))
                    return Failure();
                if (native.IsFrontmost(app))
                    return native.CanPostPaste && ClipboardUnchanged() && native.PostPaste()
                        ? PasteResult.ShortcutSent : Failure();
                if (!ClipboardUnchanged())
                    return PasteResult.ClipboardChanged;
            }
            return Failure();
        }
        finally
        {
            native.ReleaseTarget(app);
        }
    }

    public void Dispose()
    {
        disposed = true;
        targetGeneration++;
        native.ReleaseTarget(target);
        target = 0;
    }
}

internal sealed class AppKitPaste : IMacPaste
{
    private const int IgnoreOtherApps = 1 << 1;

    public nint CaptureTarget()
    {
        using var pool = new AutoreleasePool();
        var app = RunningApps.Frontmost;
        return app != 0 && !RunningApps.IsCurrentProcess(app) ? ObjC.Retain(app) : 0;
    }

    public void ReleaseTarget(nint app) => ObjC.Release(app);

    public bool TryActivate(nint app)
    {
        using var pool = new AutoreleasePool();
        if (IsTerminated(app))
            return false;
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
        return true;
    }

    public bool IsTerminated(nint app) => ObjC.Is(app, "isTerminated");

    public bool IsFrontmost(nint app)
    {
        using var pool = new AutoreleasePool();
        return RunningApps.ProcessId(app) == RunningApps.ProcessId(RunningApps.Frontmost);
    }

#if VSTAVKA_MAC_APP_STORE
    public bool CanPostPaste => false;
    public bool PostPaste() => false;
#else
    public bool CanPostPaste => MacPermissions.System.AccessibilityState == PermissionState.Granted;
    public bool PostPaste() => Accessibility.PostPaste();
#endif
}

#if !VSTAVKA_MAC_APP_STORE
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
#endif
