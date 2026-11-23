using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal enum MacLoginItemStatus : long { NotRegistered = 0, Enabled = 1, RequiresApproval = 2, NotFound = 3 }

internal sealed class MacStartupRegistration(
    Func<nint> getServiceClass,
    Func<nint, MacLoginItemStatus> getStatus,
    Func<nint, bool, bool> setEnabled) : IStartupRegistration
{
    private const string ServiceManagement = "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";
    private static readonly Lazy<nint> ServiceClass = new(FindServiceClass);

    internal static MacStartupRegistration System { get; } = new(() => ServiceClass.Value, GetStatus, SetEnabled);

    public bool IsSupported => getServiceClass() != 0;
    public bool IsEnabled => Status == MacLoginItemStatus.Enabled;

    internal MacLoginItemStatus? Status
    {
        get
        {
            var type = getServiceClass();
            return type != 0 ? getStatus(type) : null;
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        var type = getServiceClass();
        return type != 0 && setEnabled(type, enabled);
    }

    private static nint FindServiceClass()
    {
        // NativeLibrary.TryLoad uses dlopen on macOS. Keep the framework loaded for the process.
        if (!OperatingSystem.IsMacOSVersionAtLeast(13) || !NativeLibrary.TryLoad(ServiceManagement, out _))
            return 0;

        var type = ObjC.Class("SMAppService");
        if (type == 0)
            return 0;

        using var pool = new AutoreleasePool();
        var bundle = ObjC.Get(ObjC.Class("NSBundle"), "mainBundle");
        var extension = ObjC.ReadString(ObjC.Get(ObjC.Get(bundle, "bundleURL"), "pathExtension"));
        var bundleId = ObjC.ReadString(ObjC.Get(bundle, "bundleIdentifier"));
        return string.Equals(extension, "app", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(bundleId)
            ? type : 0;
    }

    private static MacLoginItemStatus GetStatus(nint type)
    {
        using var pool = new AutoreleasePool();
        var service = ObjC.Get(type, "mainAppService");
        return service == 0 ? MacLoginItemStatus.NotFound : (MacLoginItemStatus)(long)ObjC.Get(service, "status");
    }

    private static unsafe bool SetEnabled(nint type, bool enabled)
    {
        using var pool = new AutoreleasePool();
        var service = ObjC.Get(type, "mainAppService");
        var selector = ObjC.Sel(enabled ? "registerAndReturnError:" : "unregisterAndReturnError:");
        nint error = 0;
        return ObjC.Bool(service, selector, (nint)(&error)) != 0 && error == 0;
    }
}
