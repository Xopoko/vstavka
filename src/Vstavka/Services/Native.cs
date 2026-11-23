using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Vstavka.Core.Localization;

namespace Vstavka.Services;

internal static class Native
{
    internal const int ClipboardUpdate = 0x031D, Hotkey = 0x0312;
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern int CountClipboardFormats();
    [DllImport("user32.dll")] internal static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wp, nint lp);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool CloseClipboard();
    [DllImport("user32.dll")] internal static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] internal static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] internal static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] internal static extern nuint GlobalSize(nint handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern int GetCurrentPackageFullName(ref uint length, System.Text.StringBuilder? name);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal INPUTUNION data; }
    [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION { [FieldOffset(0)] internal KEYBDINPUT keyboard; [FieldOffset(0)] internal MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { internal ushort vk,scan; internal uint flags,time; internal nuint extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { internal int x,y; internal uint mouseData,flags,time; internal nuint extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { internal uint vk,scan,flags,time; internal nuint extra; }
    internal static bool IsPackaged { get { uint n=0; return GetCurrentPackageFullName(ref n,null) != 15700; } }
    internal static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    internal static uint ProcessId(nint hwnd) { GetWindowThreadProcessId(hwnd,out var id); return id; }
    internal static string ProcessName(uint processId)
    {
        try { if(processId==0)return ""; using var process=Process.GetProcessById((int)processId); return process.ProcessName; }
        catch { return ""; }
    }
    internal static string? ProcessImagePath(uint processId)
    {
        const uint queryLimitedInformation = 0x1000;
        using var process = OpenProcess(queryLimitedInformation, false, processId);
        if (process.IsInvalid)
            return null;
        var path = new StringBuilder(32768);
        var length = path.Capacity;
        return QueryFullProcessImageNameW(process, 0, path, ref length) ? path.ToString() : null;
    }
    internal static bool SendPaste()
    {
        static INPUT Key(ushort vk,bool up=false) => new() { type=1, data=new INPUTUNION { keyboard=new KEYBDINPUT { vk=vk, flags=up?2u:0u } } };
        var inputs=new[]{Key(0x11),Key(0x56),Key(0x56,true),Key(0x11,true)};
        return SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<INPUT>())==inputs.Length;
    }
}
