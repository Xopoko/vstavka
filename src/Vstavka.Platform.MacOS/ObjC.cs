using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Vstavka.Platform.MacOS;

internal static partial class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";
    private static readonly ConcurrentDictionary<string, nint> Selectors = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, nint> Classes = new(StringComparer.Ordinal);
    private static nint AppKit { get; } = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    internal static nint Class(string name)
    {
        // Keep AppKit loaded before asking libobjc for any AppKit classes.
        _ = AppKit;
        return Classes.GetOrAdd(name, GetClass);
    }

    internal static nint Sel(string name) => Selectors.GetOrAdd(name, RegisterName);
    internal static bool Responds(nint obj, string selector) => Bool(obj, Sel("respondsToSelector:"), Sel(selector)) != 0;
    internal static nint Get(nint obj, string selector) => Send(obj, Sel(selector));
    internal static nint Get(nint obj, string selector, nint argument) => Send(obj, Sel(selector), argument);
    internal static bool Is(nint obj, string selector) => Bool(obj, Sel(selector)) != 0;

    internal static unsafe nint String(string value)
    {
        // NSString preserves embedded NULs and uses a UTF-16 length, just like a .NET string.
        fixed (char* characters = value)
            return Send(Class("NSString"), Sel("stringWithCharacters:length:"), (nint)characters, value.Length);
    }

    internal static string? ReadString(nint value, int maxLength = int.MaxValue)
    {
        if (value == 0)
            return null;
        var length = Get(value, "length");
        if (length == 0)
            return string.Empty;
        if (length < 0 || length > maxLength)
            return null;
        return string.Create((int)length, value, static (characters, nativeString) =>
        {
            unsafe
            {
                fixed (char* destination = characters)
                    GetCharacters(nativeString, Sel("getCharacters:range:"), destination, new Range(0, (nuint)characters.Length));
            }
        });
    }

    internal static unsafe nint Data(byte[] bytes)
    {
        fixed (byte* data = bytes)
            return Send(Class("NSData"), Sel("dataWithBytes:length:"), (nint)data, bytes.Length);
    }

    internal static byte[]? ReadData(nint data, int maxLength)
    {
        var length = Get(data, "length");
        if (length <= 0 || length > maxLength)
            return null;
        var bytes = new byte[(int)length];
        Marshal.Copy(Get(data, "bytes"), bytes, 0, bytes.Length);
        return bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Range(nuint Location, nuint Length);

    [LibraryImport(Library, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);
    [LibraryImport(Library, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterName(string name);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint argument);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint first, nint second);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint first, nint second, nint third);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial int Int32(nint receiver, nint selector);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial byte Bool(nint receiver, nint selector);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial byte Bool(nint receiver, nint selector, nint argument);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial byte Bool(nint receiver, nint selector, nint first, nint second);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial void Void(nint receiver, nint selector);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial void Void(nint receiver, nint selector, nint argument);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial void Void(nint receiver, nint selector, byte argument);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static unsafe partial void GetCharacters(nint receiver, nint selector, char* destination, Range range);
    [LibraryImport(Library, EntryPoint = "objc_retain")]
    internal static partial nint Retain(nint value);
    [LibraryImport(Library, EntryPoint = "objc_release")]
    internal static partial void Release(nint value);
    [LibraryImport(Library, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial nint PushPool();
    [LibraryImport(Library, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void PopPool(nint pool);
}

internal readonly ref struct AutoreleasePool()
{
    private readonly nint pool = ObjC.PushPool();
    public void Dispose() => ObjC.PopPool(pool);
}
