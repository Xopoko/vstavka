using System.Buffers.Binary;

namespace Vstavka.Core;

public static class ClipboardImageLimits
{
    private const int MaxPixels = 16_000_000;

    public static bool FitsPixelLimit(long width, long height) =>
        width > 0 && height > 0 && height <= MaxPixels / width;

    public static bool FitsPngDimensions(ReadOnlySpan<byte> png) =>
        TryReadPngSize(png, out var width, out var height) && FitsPixelLimit(width, height);

    public static bool TryReadPngSize(ReadOnlySpan<byte> png, out uint width, out uint height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 45 || !png.StartsWith(signature) || !png[8..16].SequenceEqual("\0\0\0\rIHDR"u8))
            return false;

        width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        return width > 0 && height > 0;
    }
}
