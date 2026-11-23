using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Vstavka.Core;

internal static class WindowsDataProtection
{
    private const int CryptProtectUiForbidden = 1;

    internal static byte[] Protect(byte[] clearBytes) => Transform(clearBytes, true);
    internal static byte[] Unprotect(byte[] protectedBytes) => Transform(protectedBytes, false);

    private static byte[] Transform(byte[] source, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Clipboard history is protected with Windows DPAPI for the current Windows account.");

        var input = new DataBlob { Size = source.Length, Data = Marshal.AllocHGlobal(source.Length) };
        var output = default(DataBlob);
        try
        {
            Marshal.Copy(source, 0, input.Data, source.Length);
            var success = protect
                ? CryptProtectData(ref input, "Vstavka local clipboard history", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!success)
                throw new CryptographicException("Windows could not protect or unlock local clipboard history.", new Win32Exception(Marshal.GetLastWin32Error()));

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
            {
                ZeroNativeMemory(input.Data, input.Size);
                Marshal.FreeHGlobal(input.Data);
            }
            if (output.Data != IntPtr.Zero)
            {
                ZeroNativeMemory(output.Data, output.Size);
                LocalFree(output.Data);
            }
        }
    }

    private static void ZeroNativeMemory(IntPtr pointer, int count)
    {
        // Clear native DPAPI buffers before returning them to the allocator.
        var zeros = new byte[Math.Min(count, 8192)];
        for (var offset = 0; offset < count; offset += zeros.Length)
            Marshal.Copy(zeros, 0, IntPtr.Add(pointer, offset), Math.Min(zeros.Length, count - offset));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
