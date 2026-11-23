using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal static partial class CoreFoundation
{
    private const string Framework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static nint Library { get; } = NativeLibrary.Load(Framework);
    private static nint KeyCallBacks { get; } = NativeLibrary.GetExport(Library, "kCFTypeDictionaryKeyCallBacks");
    private static nint ValueCallBacks { get; } = NativeLibrary.GetExport(Library, "kCFTypeDictionaryValueCallBacks");

    internal static nint True => ReadConstant(Library, "kCFBooleanTrue");
    internal static nint ReadConstant(nint library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

    internal static nint CreateDictionary() =>
        CreateMutableDictionary(0, 0, KeyCallBacks, ValueCallBacks);

    internal static void Release(nint value)
    {
        if (value != 0)
            CFRelease(value);
    }

    [LibraryImport(Framework, EntryPoint = "CFDictionaryCreateMutable")]
    private static partial nint CreateMutableDictionary(nint allocator, nint capacity, nint keyCallbacks, nint valueCallbacks);
    [LibraryImport(Framework, EntryPoint = "CFDictionarySetValue")]
    internal static partial void SetValue(nint dictionary, nint key, nint value);
    [LibraryImport(Framework)]
    private static partial void CFRelease(nint value);
}
