using Vstavka.Core;

namespace Vstavka.Platform.MacOS;

internal sealed class NativePasteboard : IPasteboard
{
    private static nint Board => ObjC.Get(ObjC.Class("NSPasteboard"), "generalPasteboard");

    public long ChangeCount => ObjC.Get(Board, "changeCount");

    public PasteboardAccess Access => GetAccess();

    internal static PasteboardAccess GetAccess()
    {
        using var pool = new AutoreleasePool();
        var board = Board;
        return ObjC.Responds(board, "accessBehavior")
            ? (PasteboardAccess)(long)ObjC.Get(board, "accessBehavior")
            : PasteboardAccess.Legacy;
    }

    public IReadOnlyList<string> ReadTypeNames()
    {
        var types = ObjC.Get(Board, "types");
        var count = ObjC.Get(types, "count");
        var names = new List<string>();
        for (nint i = 0; i < count; i++)
            if (ObjC.ReadString(ObjC.Get(types, "objectAtIndex:", i)) is { } name)
                names.Add(name);
        return names;
    }

    public string[] ReadFilePaths()
    {
        var type = ObjC.String(ClipboardCapturePolicy.FileUrl);
        var items = ObjC.Get(Board, "pasteboardItems");
        var count = ObjC.Get(items, "count");
        var urlClass = ObjC.Class("NSURL");
        var paths = new List<string>();
        for (nint i = 0; i < count; i++)
        {
            var value = ObjC.Get(ObjC.Get(items, "objectAtIndex:", i), "stringForType:", type);
            if (value == 0)
                continue;
            var url = ObjC.Get(urlClass, "URLWithString:", value);
            if (!ObjC.Is(url, "isFileURL") || ObjC.ReadString(ObjC.Get(url, "path"), 32768) is not { Length: > 0 } path)
                continue;
            if (paths.Count == HistoryStore.MaxFilesPerItem)
                return [];
            paths.Add(path);
        }
        return paths.ToArray();
    }

    public string? ReadString(string type) =>
        ObjC.ReadString(ObjC.Get(Board, "stringForType:", ObjC.String(type)), HistoryStore.MaxTextLength);

    internal static void RequestAccess()
    {
        using var pool = new AutoreleasePool();
        // One user-initiated read triggers the access alert and adds Vstavka to Clipboard settings.
        ObjC.Get(Board, "stringForType:", ObjC.String(ClipboardCapturePolicy.Text));
    }

    public byte[]? ReadPng() =>
        ObjC.ReadData(ObjC.Get(Board, "dataForType:", ObjC.String(ClipboardCapturePolicy.Png)), HistoryStore.MaxImageBytes);

    public byte[]? ConvertTiffToPng()
    {
        var data = ObjC.Get(Board, "dataForType:", ObjC.String(ClipboardCapturePolicy.Tiff));
        if (ObjC.Get(data, "length") is <= 0 or > 64 * 1024 * 1024)
            return null;
        var representation = MacImages.Decode(data);
        return MacImages.HasSafePixelCount(representation)
            ? ObjC.ReadData(MacImages.Encode(representation, MacImages.Png), HistoryStore.MaxImageBytes)
            : null;
    }

    internal void Write(ClipItem item, bool plainText, Action<long> recordOwnWrite)
    {
        var objects = !plainText && item.Kind == ClipKind.Files ? CreateFileObjects(item.Files) : CreateItemObjects(item, plainText);
        var board = Board;
        // clearContents takes ownership and returns our changeCount. A later read can see another app's copy.
        recordOwnWrite(ObjC.Get(board, "clearContents"));
        if (ObjC.Bool(board, ObjC.Sel("writeObjects:"), objects) == 0)
            throw new InvalidOperationException("AppKit could not write the clipboard item.");
    }

    private static nint CreateFileObjects(string[] paths)
    {
        if (paths.Length == 0)
            throw new InvalidOperationException("The clipboard item contains no files.");
        var array = ObjC.Get(ObjC.Class("NSMutableArray"), "arrayWithCapacity:", paths.Length);
        var urlClass = ObjC.Class("NSURL");
        foreach (var path in paths)
        {
            if (!Path.IsPathFullyQualified(path) || path.Contains('\0'))
                throw new InvalidOperationException("A file path in the clipboard item is invalid.");
            var url = ObjC.Get(urlClass, "fileURLWithPath:", ObjC.String(path));
            if (url == 0)
                throw new InvalidOperationException("AppKit could not represent a file on the clipboard.");
            ObjC.Void(array, ObjC.Sel("addObject:"), url);
        }
        return array;
    }

    private static nint CreateItemObjects(ClipItem item, bool plainText)
    {
        var nativeItem = ObjC.Get(ObjC.Class("NSPasteboardItem"), "alloc");
        nativeItem = ObjC.Get(nativeItem, "init");
        try
        {
            if (item.Kind == ClipKind.Image && !plainText)
            {
                var png = item.ImageBytes;
                if (png is null || !ClipboardCapturePolicy.IsClipboardPng(png))
                    throw new InvalidOperationException("The clipboard image must be a PNG within Vstavka's size limits.");
                var data = ObjC.Data(png);
                var representation = MacImages.Decode(data);
                if (!MacImages.HasSafePixelCount(representation))
                    throw new InvalidOperationException("AppKit could not decode the clipboard image.");
                SetRepresentation(nativeItem, "setData:forType:", data, ClipboardCapturePolicy.Png);
                var tiff = MacImages.Encode(representation, MacImages.Tiff);
                if (tiff != 0)
                    SetRepresentation(nativeItem, "setData:forType:", tiff, ClipboardCapturePolicy.Tiff);
            }
            else
            {
                var text = item.Kind == ClipKind.Files ? string.Join(Environment.NewLine, item.Files) : item.Text;
                if (item.Kind == ClipKind.Image && text.Length == 0)
                    throw new InvalidOperationException("The clipboard image has no plain text.");
                SetRepresentation(nativeItem, "setString:forType:", ObjC.String(text), ClipboardCapturePolicy.Text);
            }
            return ObjC.Get(ObjC.Class("NSArray"), "arrayWithObject:", nativeItem);
        }
        finally
        {
            ObjC.Release(nativeItem);
        }
    }

    private static void SetRepresentation(nint item, string method, nint data, string type)
    {
        if (ObjC.Bool(item, ObjC.Sel(method), data, ObjC.String(type)) == 0)
            throw new InvalidOperationException("AppKit could not prepare a clipboard representation.");
    }
}

internal sealed class MacClipboardWriter(NativePasteboard board, ClipboardPoll poll) : IClipboardWriter
{
    public void Write(ClipItem item, bool plainText)
    {
        poll.CancelSuppression();
        ArgumentNullException.ThrowIfNull(item);
        using var pool = new AutoreleasePool();
        board.Write(item, plainText, poll.RecordOwnWrite);
    }
}
