using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vstavka.Core;

namespace Vstavka.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboardImages : IWindowsClipboardImages
{
    private const int DibV5HeaderSize = 124;
    private const uint Srgb = 0x73524742;

    public byte[]? CapturePng(ClipboardFormat format, byte[] bytes)
    {
        if (format == ClipboardFormat.Png)
        {
            if (!ValidPngDimensions(bytes))
                return null;
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return SavePng(image);
        }

        if (DibLayout.Parse(bytes) is not { } layout)
            return null;
        if (layout.HasBgraAlpha)
        {
            // Keep the straight alpha in the packed DIB. The generic BMP decoder can discard it.
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var stride = layout.Width * 4;
                var firstRow = layout.TopDown ? layout.PixelOffset : layout.PixelOffset + stride * (layout.Height - 1);
                using var bitmap = new Bitmap(layout.Width, layout.Height, layout.TopDown ? stride : -stride,
                    PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject() + firstRow);
                return SavePng(bitmap);
            }
            finally { pin.Free(); }
        }

        const int fileHeaderSize = 14;
        var bmpBytes = new byte[fileHeaderSize + bytes.Length];
        "BM"u8.CopyTo(bmpBytes);
        BinaryPrimitives.WriteInt32LittleEndian(bmpBytes.AsSpan(2), bmpBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmpBytes.AsSpan(10), fileHeaderSize + layout.PixelOffset);
        bytes.CopyTo(bmpBytes, fileHeaderSize);
        if (layout.HeaderSize >= 108)
        {
            // A DIB can reference an external color profile. Clipboard conversion must stay in memory.
            var colorSpace = bmpBytes.AsSpan(fileHeaderSize + 56);
            var profileType = BinaryPrimitives.ReadUInt32LittleEndian(colorSpace);
            if (profileType is 0x4C494E4B or 0x4D424544) // PROFILE_LINKED, PROFILE_EMBEDDED
            {
                BinaryPrimitives.WriteUInt32LittleEndian(colorSpace, Srgb);
                if (layout.HeaderSize == DibV5HeaderSize)
                    bmpBytes.AsSpan(fileHeaderSize + 112, 8).Clear();
            }
        }
        using var bmpStream = new MemoryStream(bmpBytes, writable: false);
        using var decoded = Image.FromStream(bmpStream, useEmbeddedColorManagement: false, validateImageData: true);
        return SavePng(decoded);
    }

    public byte[] CreateDibV5(byte[] png)
    {
        if (!ValidPngDimensions(png))
            throw new ArgumentException("The clipboard image has invalid or unsupported dimensions.", nameof(png));
        using var stream = new MemoryStream(png, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (image is not Bitmap bitmap)
            throw new ArgumentException("The clipboard image is not a bitmap.", nameof(png));
        var width = image.Width;
        var height = image.Height;
        if (!DibLayout.ValidDimensions(width, height))
            throw new ArgumentException("The clipboard image has invalid or unsupported dimensions.", nameof(png));
        var bounds = new Rectangle(0, 0, width, height);

        var stride = width * 4;
        var dib = new byte[DibV5HeaderSize + stride * height];
        BinaryPrimitives.WriteInt32LittleEndian(dib, DibV5HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 3); // BI_BITFIELDS
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20), stride * height);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(52), 0xFF000000);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(56), Srgb);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(108), 4); // LCS_GM_IMAGES

        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, dib, DibV5HeaderSize + row * stride, stride);
        }
        finally { bitmap.UnlockBits(data); }
        return dib;
    }

    private static bool ValidPngDimensions(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 33 && bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
        BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) == 13 && bytes[12..16].SequenceEqual("IHDR"u8) &&
        DibLayout.ValidDimensions(BinaryPrimitives.ReadInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadInt32BigEndian(bytes[20..])) &&
        BinaryPrimitives.ReadInt32BigEndian(bytes[20..]) > 0;

    private static byte[]? SavePng(Image image)
    {
        if (!DibLayout.ValidDimensions(image.Width, image.Height))
            return null;
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.Length <= HistoryStore.MaxImageBytes ? stream.ToArray() : null;
    }
}

internal readonly record struct DibLayout(int HeaderSize, int Width, int Height, int PixelOffset, bool TopDown, bool HasBgraAlpha)
{
    internal static bool ValidDimensions(int width, int height) =>
        width > 0 && height != 0 && width * Math.Abs((long)height) <= 16_000_000;

    internal static DibLayout? Parse(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 12)
            return null;
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize is not (12 or 40 or 52 or 56 or 108 or 124) || dib.Length < headerSize)
            return null;
        var core = headerSize == 12;
        var width = core ? BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]) : BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var signedHeight = core ? BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]) : BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[(core ? 8 : 12)..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[(core ? 10 : 14)..]);
        if (!ValidDimensions(width, signedHeight) || planes != 1 || bits is not (1 or 4 or 8 or 16 or 24 or 32))
            return null;

        var compression = core ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var height = Math.Abs(signedHeight);
        if (compression is not (0 or 1 or 2 or 3 or 6))
            return null;
        var maskBytes = headerSize == 40 ? compression switch { 3 => 12, 6 => 16, _ => 0 } : 0;
        var colors = core ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (colors == 0 && bits <= 8)
            colors = 1u << bits;
        var pixelOffset = headerSize + maskBytes + (long)colors * (core ? 3 : 4);
        if (pixelOffset >= dib.Length)
            return null;
        if (compression is 0 or 3 or 6)
        {
            var stride = ((long)width * bits + 31) / 32 * 4;
            if (stride * height > dib.Length - pixelOffset)
                return null;
        }
        var hasAlpha = bits == 32 && compression is 3 or 6 && (headerSize >= 56 || maskBytes == 16) &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]) == 0x00FF0000 &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]) == 0x0000FF00 &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]) == 0x000000FF &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]) == 0xFF000000;
        return new DibLayout(headerSize, width, height, (int)pixelOffset, signedHeight < 0, hasAlpha);
    }
}
