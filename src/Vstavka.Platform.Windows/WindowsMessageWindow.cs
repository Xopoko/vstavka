using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vstavka.Platform.Windows;

internal interface IWindowsMessages
{
    event Action? ClipboardChanged;
    event Action<int, uint>? HotkeyPressed;
    void StartClipboardListener();
    void StopClipboardListener();
    bool RegisterHotkey(int id, uint modifiers, uint key);
    bool UnregisterHotkey(int id);
}

internal sealed class WindowsMessageWindow : IWindowsMessages, IDisposable
{
    private readonly string className = "Vstavka.Preview.Messages." + Guid.NewGuid().ToString("N");
    private readonly NativeMethods.WindowProc windowProc;
    private nint handle;
    private nint module;
    private int threadId;
    private bool disposed;

    internal WindowsMessageWindow() => windowProc = HandleMessage;

    public event Action? ClipboardChanged;
    public event Action<int, uint>? HotkeyPressed;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (handle == 0)
                CreateWindow();
            RequireWindowThread();
            return handle;
        }
    }

    public void StartClipboardListener()
    {
        if (!NativeMethods.AddClipboardFormatListener(Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not watch the Windows clipboard.");
    }

    public void StopClipboardListener() => NativeMethods.RemoveClipboardFormatListener(Handle);
    public bool RegisterHotkey(int id, uint modifiers, uint key) => NativeMethods.RegisterHotKey(Handle, id, modifiers, key);
    public bool UnregisterHotkey(int id) => NativeMethods.UnregisterHotKey(Handle, id);

    private void CreateWindow()
    {
        threadId = Environment.CurrentManagedThreadId;
        module = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WindowClass
        {
            Procedure = windowProc,
            Instance = module,
            ClassName = className
        };
        if (NativeMethods.RegisterClass(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the Windows message window.");

        handle = NativeMethods.CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, new nint(-3),
            0, module, 0);
        if (handle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(className, module);
            throw new Win32Exception(error, "Could not create the Windows message window.");
        }
    }

    private nint HandleMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == 0x031D) // WM_CLIPBOARDUPDATE
        {
            ClipboardChanged?.Invoke();
            return 0;
        }
        if (message == 0x0312) // WM_HOTKEY
        {
            HotkeyPressed?.Invoke((int)wParam, (uint)lParam & 0x000F);
            return 0;
        }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void RequireWindowThread()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("Use the Windows message window on its owning UI thread.");
    }

    public void Dispose()
    {
        if (disposed)
            return;
        if (handle != 0)
        {
            RequireWindowThread();
            if (!NativeMethods.DestroyWindow(handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not destroy the Windows message window.");
            handle = 0;
            NativeMethods.UnregisterClass(className, module);
        }
        disposed = true;
    }
}
