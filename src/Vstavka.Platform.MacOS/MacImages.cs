using System.Runtime.InteropServices;
using Vstavka.Core;

namespace Vstavka.Platform.MacOS;

internal static partial class MacImages
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const uint PremultipliedAlphaLast = 1;
    private const int IconSize = 32;

    internal const int Tiff = 0;
    internal const int Png = 4;

    internal static nint Decode(nint data) => ObjC.Get(ObjC.Class("NSBitmapImageRep"), "imageRepWithData:", data);

    internal static nint Encode(nint bitmap, int format) =>
        ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"), format, ObjC.Get(ObjC.Class("NSDictionary"), "dictionary"));

    internal static bool HasSafePixelCount(nint bitmap)
    {
        var width = (long)ObjC.Get(bitmap, "pixelsWide");
        var height = (long)ObjC.Get(bitmap, "pixelsHigh");
        return width is > 0 and <= ClipboardCapturePolicy.MaxImagePixels &&
            height > 0 && height <= ClipboardCapturePolicy.MaxImagePixels / width;
    }

    internal static byte[]? RenderIcon(nint icon)
    {
        var original = ObjC.Send(icon, ObjC.Sel("CGImageForProposedRect:context:hints:"), 0, 0, 0);
        if (original == 0)
            return null;
        var colorSpace = CreateDeviceRgb();
        if (colorSpace == 0)
            return null;
        var context = CreateBitmapContext(0, IconSize, IconSize, 8, 4 * IconSize, colorSpace, PremultipliedAlphaLast);
        CoreFoundation.Release(colorSpace);
        if (context == 0)
            return null;
        try
        {
            DrawImage(context, new Rect(0, 0, IconSize, IconSize), original);
            var image = CreateImage(context);
            if (image == 0)
                return null;
            var bitmap = ObjC.Get(ObjC.Get(ObjC.Class("NSBitmapImageRep"), "alloc"), "initWithCGImage:", image);
            CoreFoundation.Release(image);
            try
            {
                var png = ObjC.ReadData(Encode(bitmap, Png), HistoryStore.MaxSourceIconBytes);
                return ClipboardCapturePolicy.IsSourceIconPng(png) ? png : null;
            }
            finally
            {
                ObjC.Release(bitmap);
            }
        }
        finally
        {
            CoreFoundation.Release(context);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect(double X, double Y, double Width, double Height);

    [LibraryImport(CoreGraphics, EntryPoint = "CGColorSpaceCreateDeviceRGB")]
    private static partial nint CreateDeviceRgb();
    [LibraryImport(CoreGraphics, EntryPoint = "CGBitmapContextCreate")]
    private static partial nint CreateBitmapContext(nint data, nuint width, nuint height, nuint bitsPerComponent,
        nuint bytesPerRow, nint colorSpace, uint bitmapInfo);
    [LibraryImport(CoreGraphics, EntryPoint = "CGContextDrawImage")]
    private static partial void DrawImage(nint context, Rect rectangle, nint image);
    [LibraryImport(CoreGraphics, EntryPoint = "CGBitmapContextCreateImage")]
    private static partial nint CreateImage(nint context);
}
