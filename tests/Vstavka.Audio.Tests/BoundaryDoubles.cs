// Recording boundaries only: the linked production services never call user32,
// create a window, inspect the clipboard, inject keys, or play audible sound here.
using System.Runtime.InteropServices;

namespace Vstavka.Services
{
    internal static class Native
    {
        internal const int ClipboardUpdate = 0x031D, Hotkey = 0x0312;
        internal delegate nint HookProc(int code, nint wParam, nint lParam);
        [StructLayout(LayoutKind.Sequential)]
        internal struct KBDLLHOOKSTRUCT
        {
            internal uint vk, scan, flags, time;
            internal nuint extra;
        }
        internal static readonly HashSet<int> DownKeys = [];
        internal static int ForwardedEvents;
        internal static bool IsDown(int key) => DownKeys.Contains(key);
        internal static bool AddClipboardFormatListener(nint hwnd) => true;
        internal static bool RemoveClipboardFormatListener(nint hwnd) => true;
        internal static readonly List<(int Id, uint Mods, uint Key)> HotkeyRegistrations = [];
        internal static readonly List<int> HotkeyUnregistrations = [];
        internal static bool HotkeyRegistrationSucceeds = true;
        internal static bool RegisterHotKey(nint hwnd, int id, uint mods, uint key)
        {
            HotkeyRegistrations.Add((id, mods, key));
            return HotkeyRegistrationSucceeds;
        }
        internal static bool UnregisterHotKey(nint hwnd, int id)
        {
            HotkeyUnregistrations.Add(id);
            return true;
        }
        internal static nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread) => 1;
        internal static bool UnhookWindowsHookEx(nint hook) => true;
        internal static nint GetModuleHandle(string? name) => 0;
        internal static nint CallNextHookEx(nint hook, int code, nint wp, nint lp)
        {
            ForwardedEvents++;
            return 73;
        }
    }
}

namespace System.Windows.Interop
{
    internal delegate nint HwndSourceHook(nint hwnd, int msg, nint wp, nint lp, ref bool handled);
    internal sealed class HwndSourceParameters(string name)
    {
        internal string Name { get; } = name;
        internal nint ParentWindow { get; set; }
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal int WindowStyle { get; set; }
    }
    internal sealed class HwndSource(HwndSourceParameters parameters) : IDisposable
    {
        internal nint Handle => 1;
        internal HwndSourceParameters Parameters { get; } = parameters;
        internal void AddHook(HwndSourceHook callback) { }
        public void Dispose() { }
    }
}

namespace System.Windows.Threading
{
    internal enum DispatcherPriority { Send }
    internal sealed class Dispatcher
    {
        private readonly Queue<Action> pending = [];
        internal static Dispatcher CurrentDispatcher => new();
        internal static void Run() { }
        internal void BeginInvoke(Action action) => pending.Enqueue(action);
        internal void BeginInvokeShutdown(DispatcherPriority priority) { }
        internal void Drain()
        {
            while (pending.TryDequeue(out var action)) action();
        }
    }
}

namespace System.Media
{
    internal sealed class SoundPlayer(Stream stream) : IDisposable
    {
        internal static readonly List<SoundPlayer> Created = [];
        internal Stream Stream { get; } = Register(stream);
        internal int Plays { get; private set; }
        internal bool Loaded { get; private set; }
        internal bool Disposed { get; private set; }
        private static Stream Register(Stream stream) => stream;
        internal void Load()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Loaded = true;
            Created.Add(this);
        }
        internal void Play()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Plays++;
        }
        public void Dispose() => Disposed = true;
    }
}
