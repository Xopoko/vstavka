using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vstavka.Core;

namespace Vstavka.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSourceIcons : IWindowsSourceIcons
{
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> cache = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? GetPng(string executablePath) =>
        cache.GetOrAdd(executablePath, path => new Lazy<byte[]?>(() => Extract(path))).Value;

    private static byte[]? Extract(string executablePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
                return null;
            using var sized = new Icon(icon, new Size(32, 32));
            using var pixels = sized.ToBitmap();
            using var resized = pixels.Width == 32 && pixels.Height == 32 ? null : new Bitmap(pixels, 32, 32);
            using var stream = new MemoryStream();
            (resized ?? pixels).Save(stream, ImageFormat.Png);
            return stream.Length <= HistoryStore.MaxSourceIconBytes ? stream.ToArray() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ExternalException or
            ArgumentException or InvalidOperationException or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }
    }
}
