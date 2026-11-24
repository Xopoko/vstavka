using System.Buffers.Binary;
using System.IO.Compression;

namespace Vstavka.Core;

internal static class SourceIconPng
{
    internal static bool IsValid(ReadOnlySpan<byte> png)
    {
        if (png.Length > HistoryStore.MaxSourceIconBytes ||
            !ClipboardImageLimits.TryReadPngSize(png, out var width, out var height) || width > 32 || height > 32)
            return false;
        var header = png[8..];
        // Application icon encoders write small, non-interlaced PNGs.
        if (header[18] != 0 || header[19] != 0 || header[20] != 0)
            return false;
        var depth = header[16];
        var color = header[17];
        var channels = color switch
        {
            0 when depth is 1 or 2 or 4 or 8 or 16 => 1,
            2 when depth is 8 or 16 => 3,
            3 when depth is 1 or 2 or 4 or 8 => 1,
            4 when depth is 8 or 16 => 2,
            6 when depth is 8 or 16 => 4,
            _ => 0
        };
        if (channels == 0 || !HasValidChecksum(header.Slice(4, 17), header[21..]))
            return false;

        using var compressed = new MemoryStream();
        bool hasPalette = false;
        bool imageDataEnded = false;
        for (var remaining = header[25..]; remaining.Length >= 12;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
            if (length > remaining.Length - 12)
                return false;
            var kind = remaining[4..8];
            var data = remaining.Slice(8, (int)length);
            var checksumOffset = 8 + data.Length;
            if (!HasValidChecksum(remaining[4..checksumOffset], remaining[checksumOffset..]))
                return false;
            remaining = remaining[(checksumOffset + 4)..];
            if (kind.SequenceEqual("IDAT"u8))
            {
                if (imageDataEnded || (color == 3 && !hasPalette))
                    return false;
                compressed.Write(data);
                continue;
            }
            if (kind.SequenceEqual("IEND"u8))
            {
                var rowLength = 1 + ((int)width * channels * depth + 7) / 8;
                return data.IsEmpty && remaining.IsEmpty && compressed.Length > 0 &&
                    HasValidPixels(compressed, rowLength, (int)height);
            }
            imageDataEnded |= compressed.Length > 0;
            if (kind.SequenceEqual("PLTE"u8))
            {
                if (hasPalette || imageDataEnded || color is 0 or 4 || length is < 3 or > 768 ||
                    length % 3 != 0 || (color == 3 && length / 3 > (1 << depth)))
                    return false;
                hasPalette = true;
            }
            else if ((kind[0] & 32) == 0)
                return false;
        }
        return false;
    }

    private static bool HasValidPixels(MemoryStream compressed, int rowLength, int height)
    {
        compressed.Position = 0;
        var expected = rowLength * height;
        Span<byte> pixels = stackalloc byte[expected + 1];
        try
        {
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            if (zlib.ReadAtLeast(pixels, pixels.Length, throwOnEndOfStream: false) != expected)
                return false;
        }
        catch (InvalidDataException) { return false; }
        for (var offset = 0; offset < expected; offset += rowLength)
            if (pixels[offset] > 4)
                return false;
        return true;
    }

    private static bool HasValidChecksum(ReadOnlySpan<byte> chunk, ReadOnlySpan<byte> checksum)
    {
        var crc = uint.MaxValue;
        foreach (var value in chunk)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (0u - (crc & 1u)));
        }
        return ~crc == BinaryPrimitives.ReadUInt32BigEndian(checksum);
    }
}
