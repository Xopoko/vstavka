#if VSTAVKA_MAC_APP_STORE
using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal sealed class MacStoreReview : IStoreReview
{
    private const string StoreKit = "/System/Library/Frameworks/StoreKit.framework/StoreKit";
    private static readonly Lazy<nint> ReviewController = new(FindReviewController);
    private readonly Lazy<string?> _appVersion = new(ReadAppVersion);

    public string? AppVersion => _appVersion.Value;

    public bool TryRequest()
    {
        try
        {
            using var pool = new AutoreleasePool();
            var controller = ReviewController.Value;
            if (controller == 0)
                return false;
            ObjC.Void(controller, ObjC.Sel("requestReview"));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static nint FindReviewController()
    {
        // Keep the framework loaded for the lifetime of the Objective-C class.
        if (!NativeLibrary.TryLoad(StoreKit, out _))
            return 0;
        var controller = ObjC.Class("SKStoreReviewController");
        return controller != 0 && ObjC.Responds(controller, "requestReview") ? controller : 0;
    }

    private static string? ReadAppVersion()
    {
        try
        {
            using var pool = new AutoreleasePool();
            var bundle = ObjC.Get(ObjC.Class("NSBundle"), "mainBundle");
            var value = ObjC.Get(bundle, "objectForInfoDictionaryKey:", ObjC.String("CFBundleShortVersionString"));
            return ObjC.ReadString(value);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
#endif
