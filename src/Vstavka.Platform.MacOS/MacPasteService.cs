using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal sealed class MacPasteService : IPasteService, IDisposable
{
    private const int IgnoreOtherApps = 1 << 1;
    private nint target;
#if !VSTAVKA_MAC_APP_STORE
    private int targetGeneration;
#endif
    private bool disposed;

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
#if !VSTAVKA_MAC_APP_STORE
        targetGeneration++;
#endif
    }

    public Task<PasteResult> PasteAsync()
    {
        if (disposed || target == 0)
            return Task.FromResult(PasteResult.Manual);

        var app = target;
        target = 0;
        try
        {
            using var pool = new AutoreleasePool();
            if (ObjC.Is(app, "isTerminated"))
                return Task.FromResult(PasteResult.Manual);
            Activate(app);

#if !VSTAVKA_MAC_APP_STORE
            if (MacPermissions.System.AccessibilityState == PermissionState.Granted)
                return PostPasteWhenActiveAsync(app, ++targetGeneration);
#endif
            return Task.FromResult(PasteResult.Manual);
        }
        finally
        {
            ObjC.Release(app);
        }
    }

#if !VSTAVKA_MAC_APP_STORE
    private async Task<PasteResult> PostPasteWhenActiveAsync(nint app, int generation)
    {
        ObjC.Retain(app);
        try
        {
            for (var i = 0; i < 10; i++)
            {
                // Resume on the UI context so AppKit can process the activation between checks.
                await Task.Delay(50);
                if (generation != targetGeneration)
                    return PasteResult.Manual;
                using var pool = new AutoreleasePool();
                if (ObjC.Is(app, "isTerminated"))
                    return PasteResult.Manual;
                if (RunningApps.ProcessId(app) == RunningApps.ProcessId(RunningApps.Frontmost))
                    return MacPermissions.System.AccessibilityState == PermissionState.Granted && Accessibility.PostPaste()
                        ? PasteResult.ShortcutSent
                        : PasteResult.Manual;
            }
            return PasteResult.Manual;
        }
        finally
        {
            ObjC.Release(app);
        }
    }
#endif

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

    public void Dispose()
    {
        disposed = true;
#if !VSTAVKA_MAC_APP_STORE
        targetGeneration++;
#endif
        ObjC.Release(target);
        target = 0;
    }
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
