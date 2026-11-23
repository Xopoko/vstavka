using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Vstavka.Services;

internal sealed class SourceAppIcons
{
    private readonly Dictionary<string, Task<byte[]?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Called on the clipboard dispatcher. Cache misses and failures alike for this session.
    internal Task<byte[]?> GetAsync(string source, uint processId)
    {
        if (processId == 0 || string.IsNullOrWhiteSpace(source))
            return Task.FromResult<byte[]?>(null);
        if (!_cache.TryGetValue(source, out var extraction))
            _cache.Add(source, extraction = Task.Run(() => Extract(source, processId)));
        return extraction;
    }

    private static byte[]? Extract(string source, uint processId)
    {
        try
        {
            var path = Native.ProcessImagePath(processId);
            if (path is null || !string.Equals(Path.GetFileNameWithoutExtension(path), source, StringComparison.OrdinalIgnoreCase))
                return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            using var sizedIcon = new Icon(icon, new Size(32, 32));
            using var pixels = sizedIcon.ToBitmap();
            using var resized = pixels.Width == 32 && pixels.Height == 32 ? null : new Bitmap(pixels, 32, 32);
            using var png = new MemoryStream();
            (resized ?? pixels).Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException or
            ArgumentException or InvalidOperationException or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }
    }
}
