using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vstavka.Core;

namespace Vstavka.Platform.Windows;

internal enum ClipboardFormat { Text, Files, Dib, DibV5, Png, Exclude, ViewerIgnore, IncludeInHistory, UploadToCloud }
internal enum ClipboardWriteResult { Written, Busy, Changed, Failed }
internal readonly record struct ClipboardOwner(string Name, string? Path = null);
internal readonly record struct ClipboardData(ClipboardFormat Format, byte[] Bytes);

internal interface IClipboardRead : IDisposable
{
    ClipboardOwner Owner { get; }
    string ForegroundProcessName { get; }
    bool Has(ClipboardFormat format);
    nuint Size(ClipboardFormat format);
    int? ReadFlag(ClipboardFormat format);
    byte[]? ReadBytes(ClipboardFormat format, int maxBytes);
    string[]? ReadFiles(out bool tooMany);
    byte[]? ReadBitmap();
}

internal interface IWindowsClipboard
{
    uint Sequence { get; }
    IClipboardRead? TryOpenRead();
    ClipboardWriteResult TryWrite(uint expectedSequence, IReadOnlyList<ClipboardData> data, out uint writtenSequence);
}

internal sealed class Win32Clipboard(WindowsMessageWindow window) : IWindowsClipboard
{
    private readonly Lazy<Dictionary<ClipboardFormat, uint>> formats = new(RegisterFormats);

    public uint Sequence => NativeMethods.GetClipboardSequenceNumber();

    public IClipboardRead? TryOpenRead()
    {
        var registeredFormats = formats.Value;
        return NativeMethods.OpenClipboard(0) ? new ReadSession(registeredFormats) : null;
    }

    public ClipboardWriteResult TryWrite(uint expectedSequence, IReadOnlyList<ClipboardData> data, out uint writtenSequence)
    {
        writtenSequence = 0;
        var hwnd = window.Handle;
        var registeredFormats = formats.Value;
        var handles = new nint[data.Count];
        if (!NativeMethods.OpenClipboard(hwnd))
            return ClipboardWriteResult.Busy;
        try
        {
            if (Sequence != expectedSequence)
                return ClipboardWriteResult.Changed;
            for (var index = 0; index < data.Count; index++)
            {
                var bytes = data[index].Bytes;
                var global = handles[index] = NativeMethods.GlobalAlloc(0x0042, (nuint)bytes.Length); // GMEM_MOVEABLE | GMEM_ZEROINIT
                var address = global == 0 ? 0 : NativeMethods.GlobalLock(global);
                if (address == 0)
                    return ClipboardWriteResult.Failed;
                try { Marshal.Copy(bytes, 0, address, bytes.Length); }
                finally { NativeMethods.GlobalUnlock(global); }
            }
            if (!NativeMethods.EmptyClipboard())
                return ClipboardWriteResult.Failed;
            for (var index = 0; index < data.Count; index++)
            {
                if (NativeMethods.SetClipboardData(registeredFormats[data[index].Format], handles[index]) == 0)
                    return ClipboardWriteResult.Failed;
                handles[index] = 0; // The clipboard now owns this block.
            }
            writtenSequence = Sequence;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            foreach (var handle in handles)
                if (handle != 0)
                    NativeMethods.GlobalFree(handle);
        }
        // The system can synthesize formats at close. Check ownership around the resulting sequence.
        var closedSequence = Sequence;
        if (NativeMethods.GetClipboardOwner() == hwnd && Sequence == closedSequence)
            writtenSequence = closedSequence;
        return ClipboardWriteResult.Written;
    }

    private static Dictionary<ClipboardFormat, uint> RegisterFormats() => new()
    {
        [ClipboardFormat.Text] = 13, // CF_UNICODETEXT
        [ClipboardFormat.Files] = 15, // CF_HDROP
        [ClipboardFormat.Dib] = 8, // CF_DIB
        [ClipboardFormat.DibV5] = 17, // CF_DIBV5
        [ClipboardFormat.Png] = Register("PNG"),
        [ClipboardFormat.Exclude] = Register("ExcludeClipboardContentFromMonitorProcessing"),
        [ClipboardFormat.ViewerIgnore] = Register("Clipboard Viewer Ignore"),
        [ClipboardFormat.IncludeInHistory] = Register("CanIncludeInClipboardHistory"),
        [ClipboardFormat.UploadToCloud] = Register("CanUploadToCloudClipboard")
    };

    private static uint Register(string name)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        return format != 0 ? format : throw new Win32Exception(Marshal.GetLastWin32Error(),
            $"Could not register the Windows clipboard format '{name}'.");
    }

    private sealed class ReadSession(Dictionary<ClipboardFormat, uint> registeredFormats) : IClipboardRead
    {
        public ClipboardOwner Owner => SourceOf(NativeMethods.GetClipboardOwner(), includePath: true);
        public string ForegroundProcessName => SourceOf(NativeMethods.GetForegroundWindow(), includePath: false).Name;
        public bool Has(ClipboardFormat format) => NativeMethods.IsClipboardFormatAvailable(registeredFormats[format]);
        public nuint Size(ClipboardFormat format) => Has(format)
            ? NativeMethods.GlobalSize(NativeMethods.GetClipboardData(registeredFormats[format])) : 0;

        public int? ReadFlag(ClipboardFormat format)
        {
            var handle = NativeMethods.GetClipboardData(registeredFormats[format]);
            if (handle == 0 || NativeMethods.GlobalSize(handle) < sizeof(int))
                return null;
            var address = NativeMethods.GlobalLock(handle);
            if (address == 0)
                return null;
            try { return Marshal.ReadInt32(address); }
            finally { NativeMethods.GlobalUnlock(handle); }
        }

        public byte[]? ReadBytes(ClipboardFormat format, int maxBytes)
        {
            if (!Has(format))
                return null;
            var handle = NativeMethods.GetClipboardData(registeredFormats[format]);
            var size = NativeMethods.GlobalSize(handle);
            if (size == 0 || size > (nuint)maxBytes)
                return null;
            var address = NativeMethods.GlobalLock(handle);
            if (address == 0)
                return null;
            try
            {
                var bytes = new byte[(int)size];
                Marshal.Copy(address, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { NativeMethods.GlobalUnlock(handle); }
        }

        public string[]? ReadFiles(out bool tooMany)
        {
            tooMany = false;
            var handle = NativeMethods.GetClipboardData(registeredFormats[ClipboardFormat.Files]);
            if (handle == 0 || NativeMethods.GlobalSize(handle) < 20)
                return null;
            var count = NativeMethods.DragQueryFile(handle, uint.MaxValue, null, 0);
            if (count > HistoryStore.MaxFilesPerItem)
            {
                tooMany = true;
                return null;
            }
            var files = new string[count];
            for (var index = 0u; index < count; index++)
            {
                var length = NativeMethods.DragQueryFile(handle, index, null, 0);
                if (length is 0 or > 32768)
                    return null;
                var path = new char[length + 1];
                if (NativeMethods.DragQueryFile(handle, index, path, (uint)path.Length) != length)
                    return null;
                files[index] = new string(path, 0, (int)length);
            }
            return files;
        }

        public byte[]? ReadBitmap()
        {
            const uint bitmapFormat = 2; // CF_BITMAP
            if (!NativeMethods.IsClipboardFormatAvailable(bitmapFormat))
                return null;
            var bitmap = NativeMethods.GetClipboardData(bitmapFormat);
            if (bitmap == 0 ||
                NativeMethods.GetBitmapObject(bitmap, Marshal.SizeOf<NativeMethods.Bitmap>(), out var description) == 0 ||
                !DibLayout.ValidDimensions(description.Width, description.Height))
                return null;
            var info = new NativeMethods.BitmapInfoHeader
            {
                Size = 40, Width = description.Width, Height = -description.Height, Planes = 1, BitCount = 32
            };
            var pixels = new byte[description.Width * description.Height * 4];
            var dc = NativeMethods.GetDC(0);
            if (dc == 0)
                return null;
            try
            {
                if (NativeMethods.GetDIBits(dc, bitmap, 0, (uint)description.Height, pixels, ref info, 0) != description.Height)
                    return null;
            }
            finally { NativeMethods.ReleaseDC(0, dc); }

            var dib = new byte[40 + pixels.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
            BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), description.Width);
            BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -description.Height);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
            pixels.CopyTo(dib, 40);
            return dib;
        }

        public void Dispose() => NativeMethods.CloseClipboard();

        private static ClipboardOwner SourceOf(nint hwnd, bool includePath)
        {
            var id = NativeMethods.ProcessId(hwnd);
            if (id == 0)
                return new ClipboardOwner("");
            string? name = null;
            try
            {
                using var process = Process.GetProcessById((int)id);
                name = process.ProcessName;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { }

            string? path = null;
            if (includePath || name is null)
            {
                using var process = NativeMethods.OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
                if (!process.IsInvalid)
                {
                    var buffer = new StringBuilder(32768);
                    var length = buffer.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref length))
                        path = buffer.ToString();
                }
            }
            var exeName = Path.GetFileNameWithoutExtension(path);
            name ??= exeName ?? "";
            return new ClipboardOwner(name, string.Equals(name, exeName, StringComparison.OrdinalIgnoreCase) ? path : null);
        }
    }
}
