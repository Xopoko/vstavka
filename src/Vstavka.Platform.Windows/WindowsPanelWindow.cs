using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vstavka.Platform.Windows;

internal sealed class WindowsPanelWindow : IPanelWindow
{
    private const int ExtendedStyle = -20;
    private const int ToolWindow = 0x00000080, AppWindow = 0x00040000, NoActivate = 0x08000000;
    private const uint NoSizeOrMove = 0x0003, NoActivatePosition = 0x0010, FrameChanged = 0x0020;

    private nint handle;
    private int showGeneration;

    public void Attach(nint hwnd, string descriptor)
    {
        if (hwnd == 0 || descriptor != "HWND")
            throw new ArgumentException("The Windows panel requires a valid HWND.", nameof(hwnd));
        var style = nint.Size == 8
            ? (int)NativeMethods.GetWindowLongPtr(hwnd, ExtendedStyle)
            : NativeMethods.GetWindowLong(hwnd, ExtendedStyle);
        var error = Marshal.GetLastWin32Error();
        if (style == 0 && error != 0)
            throw new Win32Exception(error);
        var desiredStyle = (style | ToolWindow) & ~(AppWindow | NoActivate);
        if (desiredStyle != style)
        {
            var previous = nint.Size == 8
                ? NativeMethods.SetWindowLongPtr(hwnd, ExtendedStyle, desiredStyle)
                : NativeMethods.SetWindowLong(hwnd, ExtendedStyle, desiredStyle);
            error = Marshal.GetLastWin32Error();
            if (previous == 0 && error != 0)
                throw new Win32Exception(error);
        }
        if (!NativeMethods.SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0,
            NoSizeOrMove | NoActivatePosition | FrameChanged))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        showGeneration++;
        handle = hwnd;
    }

    public void Showing()
    {
        var generation = ++showGeneration;
        if (handle == 0)
            return;
        if (NativeMethods.IsWindowVisible(handle))
            Activate();
        else
            SynchronizationContext.Current?.Post(_ => Activate(), null);

        void Activate()
        {
            if (generation != showGeneration || !NativeMethods.IsWindowVisible(handle))
                return;
            NativeMethods.SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, NoSizeOrMove | NoActivatePosition);
            if (NativeMethods.GetForegroundWindow() == handle || NativeMethods.SetForegroundWindow(handle))
            {
                var focused = NativeMethods.GetFocus();
                if (focused != handle && !NativeMethods.IsChild(handle, focused))
                    NativeMethods.SetFocus(handle);
            }
        }
    }

    public void Hidden() => showGeneration++;
}
