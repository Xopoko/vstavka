using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Services;

internal sealed class SystemBridge : IDisposable
{
    private readonly HwndSource source;
    private readonly Native.HookProc callback;
    private readonly Dispatcher dispatcher;
    private nint hook;
    private Thread? hookThread;
    private Dispatcher? hookDispatcher;
    private int hotkeyId;
    private long lastPaste;
    private bool pasteKeyHeld;
    internal event Action? ClipboardChanged;
    internal event Action? HotkeyPressed;
    internal event Action? PasteShortcutPressed;
    internal bool PasteFeedbackAvailable => hook != 0;
    internal SystemBridge(Dispatcher dispatcher)
    {
        this.dispatcher=dispatcher;
        source=new HwndSource(new HwndSourceParameters("Vstavka.SystemBridge") { ParentWindow=new nint(-3),Width=0,Height=0,WindowStyle=0 });
        source.AddHook(WndProc);
        callback=KeyboardProc;
    }
    internal bool Start(string hotkey)
    {
        if(!Native.AddClipboardFormatListener(source.Handle)) throw new InvalidOperationException(Strings.Get("ClipboardListenerFailed"));
        var ready=new ManualResetEventSlim(false);
        hookThread=new Thread(()=>
        {
            hookDispatcher=Dispatcher.CurrentDispatcher;
            hook=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);
            ready.Set();
            Dispatcher.Run();
            if(hook!=0)Native.UnhookWindowsHookEx(hook);
            hook=0;
        }) {IsBackground=true,Name="Vstavka.PasteFeedback"};
        hookThread.SetApartmentState(ApartmentState.STA);
        hookThread.Start();ready.Wait(2000);
        return SetHotkey(hotkey);
    }
    internal bool SetHotkey(string hotkey)
    {
        var mods=AppSettings.NormalizeHotkey(hotkey) switch {"Alt+V"=>1u,"Ctrl+Alt+V"=>3u,_=>6u};
        // Keep the reserved replacement registered; no unprotected gap or retry.
        var nextId=hotkeyId==1?2:1;
        if(!Native.RegisterHotKey(source.Handle,nextId,mods|0x4000,0x56)) return false;
        if(hotkeyId!=0)Native.UnregisterHotKey(source.Handle,hotkeyId);
        hotkeyId=nextId;
        return true;
    }
    private nint WndProc(nint hwnd,int msg,nint wp,nint lp,ref bool handled)
    {
        if(msg==Native.ClipboardUpdate) ClipboardChanged?.Invoke();
        else if(msg==Native.Hotkey) { handled=true; HotkeyPressed?.Invoke(); }
        return 0;
    }
    private nint KeyboardProc(int code,nint wp,nint lp)
    {
        if(code>=0)
        {
            var data=Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lp);
            if(data.vk==0x56 || data.vk==0x2D)
            {
                var up=wp==0x101 || wp==0x105;
                if(up) pasteKeyHeld=false;
                else if((wp==0x100 || wp==0x104) && (data.flags&0x10)==0)
                {
                    var paste=(data.vk==0x56 && Native.IsDown(0x11) && !Native.IsDown(0x10)) || (data.vk==0x2D && Native.IsDown(0x10) && !Native.IsDown(0x11));
                    if(paste && !pasteKeyHeld && !Native.IsDown(0x12) && !Native.IsDown(0x5B) && !Native.IsDown(0x5C))
                    {
                        pasteKeyHeld=true;
                        var now=Environment.TickCount64;
                        if(now-lastPaste>80) { lastPaste=now; dispatcher.BeginInvoke(new Action(()=>PasteShortcutPressed?.Invoke())); }
                    }
                }
            }
        }
        return Native.CallNextHookEx(hook,code,wp,lp);
    }
    public void Dispose()
    {
        Native.RemoveClipboardFormatListener(source.Handle);
        Native.UnregisterHotKey(source.Handle,1);
        Native.UnregisterHotKey(source.Handle,2);
        hookDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        hookThread?.Join(1000);
        source.Dispose();
    }
}
