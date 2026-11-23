using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Presentation;
using Vstavka.Services;
using Vstavka.Shell;
using Vstavka.Views;
using Forms=System.Windows.Forms;

namespace Vstavka;

public partial class App : System.Windows.Application
{
    private HistoryStore store=null!;
    private RibbonWindow ribbon=null!;
    private ClipboardService clipboard=null!;
    private SystemBridge? bridge;
    private readonly SoundService sounds=new();
    private readonly SourceAppIcons sourceAppIcons=new();
    private Forms.NotifyIcon? tray;
    private Mutex? mutex;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    private nint pasteTarget;
    private string? pendingClipboardError;
    private bool started, demo, testSession, pasteBusy, settingsOpen;
    private Process? testPad;
    private readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer retentionTimer=new(){Interval=TimeSpan.FromMinutes(30)};
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Contains("--self-test")) { Shutdown(Diagnostics.Run(e.Args));return; }
        if(e.Args.Contains("--render-preview")) { Shutdown(Diagnostics.Render(e.Args));return; }
        if(e.Args.Contains("--test-pad")) {var pad=new TestPadWindow();pad.Closed+=(_,_)=>Shutdown();pad.Show();return;}
        demo=e.Args.Contains("--demo");
        testSession=e.Args.Contains("--test-session");
        try
        {
            if(!demo && !testSession)
            {
                var instanceKey="Local\\Vstavka."+System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
                mutex=new Mutex(true,instanceKey,out var first);
                activation=new EventWaitHandle(false,EventResetMode.AutoReset,instanceKey+".Activate");
                if(!first) { activation.Set(); Shutdown(); return; }
                activationWait=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>Dispatcher.BeginInvoke(new Action(OpenRibbon)),null,-1,false);
            }
            var dataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Vstavka");
            if(demo) dataDirectory=Path.Combine(Path.GetTempPath(),"Vstavka.Demo."+Guid.NewGuid().ToString("N"));
            if(testSession)dataDirectory=Path.Combine(Path.GetTempPath(),"Vstavka.Test."+Guid.NewGuid().ToString("N"));
            store=new HistoryStore(dataDirectory);store.Load();
            if(store.SavingBlocked)
            {
                FailStartup(store.LoadWarning!);
                return;
            }
            Strings.SetLanguage(store.Settings.Language);
            ThemeManager.Apply(store.Settings.Theme,store.Settings.Accent);
            var consent=Path.Combine(dataDirectory,"history-consent-v1");
            if(!demo && !testSession && !File.Exists(consent))
            {
                if(e.Args.Contains("--background") || e.Args.Contains("--startup")) {Shutdown();return;}
                var welcome=new WelcomeWindow(store.Settings.Hotkey);
                if(welcome.ShowDialog()!=true) {Shutdown();return;}
                store.Settings.StartAtLogin=welcome.StartAtLogin;
                store.Settings.Language=Strings.Language;
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(consent,"Local clipboard history enabled by user. Notice version 1.");
            }
            ribbon=new RibbonWindow(store.GetSourceIcon,store);MainWindow=ribbon;
            sounds.SetVolume(store.Settings.Volume);
            clipboard=new ClipboardService(()=>store.Settings);
            if(testSession)
            {
                testPad=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--test-pad"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
                clipboard.TestSourceProcessId=(uint)testPad!.Id;
                testPad.EnableRaisingEvents=true;
                testPad.Exited+=(_,_)=>Dispatcher.BeginInvoke(new Action(RequestExit));
                store.Settings.StartAtLogin=false;
            }
            clipboard.CopyDetected+=()=>{if(store.Settings.SoundEnabled)sounds.Copy();};
            clipboard.Captured+=async(item,sourceProcessId)=>
            {
                try
                {
                    store.Add(item);
                    if(store.LastRejectionReason!=null) {ShowClipboardError(store.LastRejectionReason);return;}
                    if(ribbon.IsVisible)Refresh();
                    QueueSave();
                    var source=item.SourceApp;
                    var png=await sourceAppIcons.GetAsync(source,sourceProcessId);
                    if(started && png is not null && store.SetSourceIcon(source,png))
                    {
                        if(ribbon.IsVisible)Refresh();
                        QueueSave();
                    }
                }
                catch { ShowClipboardError(Strings.Get("CopyFailed")); }
            };
            clipboard.Status+=ShowClipboardError;
            ribbon.PasteRequested+=async(item,plain)=>await PasteAsync(item,plain);
            ribbon.CopyRequested+=async item=>
            {
                if(demo){SetStatus(Strings.Get("DemoCopy"));return;}
                if(await clipboard.WriteAsync(item,false) is null)return;
                if(store.Settings.SoundEnabled)sounds.Copy();
                SetStatus(Strings.Get("Copied"));
            };
            ribbon.PinRequested+=item=>{store.TogglePin(item.Id);Refresh();QueueSave();};
            ribbon.DeleteRequested+=item=>{store.Remove(item.Id);Refresh();QueueSave();};
            ribbon.CollectionsChanged+=()=>{RefreshStatus();QueueSave();};
            ribbon.SettingsRequested+=OpenSettings;
            ribbon.PauseRequested+=TogglePause;
            saveTimer.Tick+=async(_,_)=>{saveTimer.Stop();try{await store.SaveAsync();}catch{Notify(Strings.Get("SaveFailed"));}};
            retentionTimer.Tick+=(_,_)=>{store.ApplyRetention();if(ribbon.IsVisible)Refresh();QueueSave();};
            started=true;
            if(demo)
            {
                foreach(var item in Diagnostics.SampleItems())store.Add(item);
                foreach(var (source,png) in PreviewSourceIcons.Create())store.SetSourceIcon(source,png);
                Refresh();ribbon.OpenDrawer();SetStatus(Strings.Get("DemoStatus"));
            }
            else
            {
                CreateTray();
                bridge=new SystemBridge(Dispatcher);
                bridge.ClipboardChanged+=clipboard.OnClipboardChanged;
                bridge.HotkeyPressed+=()=>{if(ribbon.IsVisible)ribbon.DismissDrawer();else OpenRibbon();};
                bridge.PasteShortcutPressed+=()=>{if(store.Settings.PasteSoundEnabled && !ribbon.IsActive && !settingsOpen)sounds.Paste();};
                if(!bridge.Start(store.Settings.Hotkey))Notify(Strings.Get("HotkeyBusy"));
                if(!bridge.PasteFeedbackAvailable)Notify(Strings.Get("PasteFeedbackUnavailable"));
                retentionTimer.Start();
                var startupWarning=testSession?null:await StartupService.SetEnabledAsync(store.Settings.StartAtLogin);
                if(startupWarning!=null)Notify(startupWarning);
                Save();
                if(!e.Args.Contains("--background")&&!e.Args.Contains("--startup"))OpenRibbon();
                if(store.LoadWarning!=null)Notify(store.LoadWarning);
            }
        }
        catch(Exception ex)
        {
            FailStartup(ex.GetType().Name);
        }
    }
    private void FailStartup(string reason)
    {
        System.Windows.MessageBox.Show(Strings.Format("StartFailed",reason),Strings.Get("AppName"),MessageBoxButton.OK,MessageBoxImage.Information);
        Shutdown(1);
    }
    private void CreateTray()
    {
        var iconPath=System.IO.Path.Combine(AppContext.BaseDirectory,"Vstavka.exe");
        tray=new Forms.NotifyIcon {Text=Strings.Get("AppName")+" · "+store.Settings.Hotkey,Icon=System.Drawing.Icon.ExtractAssociatedIcon(iconPath)??System.Drawing.SystemIcons.Application,Visible=true};
        RebuildTrayMenu();
        tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(new Action(OpenRibbon));
    }
    private void RebuildTrayMenu()
    {
        if(tray==null)return;
        var oldMenu=tray.ContextMenuStrip;
        var menu=new Forms.ContextMenuStrip();
        menu.RightToLeft=Strings.IsRightToLeft?Forms.RightToLeft.Yes:Forms.RightToLeft.No;
        menu.Items.Add(Strings.Get("TrayOpen"),null,(_,_)=>Dispatcher.BeginInvoke(new Action(OpenRibbon)));
        menu.Items.Add(Strings.Get("Settings"),null,(_,_)=>Dispatcher.BeginInvoke(new Action(OpenSettings)));
        var pause=new Forms.ToolStripMenuItem(Strings.Get(clipboard.Paused?"Resume":"Pause"));
        pause.Click+=(_,_)=>Dispatcher.Invoke(()=>{TogglePause();pause.Text=Strings.Get(clipboard.Paused?"Resume":"Pause");});menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Get("Exit"),null,(_,_)=>Dispatcher.Invoke(RequestExit));tray.ContextMenuStrip=menu;
        oldMenu?.Dispose();
    }
    private void OpenRibbon()
    {
        if(!started || settingsOpen)return;
        var foreground=Native.GetForegroundWindow();
        if(Native.ProcessId(foreground)!=Environment.ProcessId)pasteTarget=foreground;
        Refresh();ribbon.OpenDrawer();
        if(pendingClipboardError is { } error)
        {
            SetStatus(error);
            pendingClipboardError=null;
        }
    }
    private void Refresh()
    {
        ribbon.UpdateLanguage();
        ribbon.SetItems(store.Items);
        RefreshStatus();
    }
    private void RefreshStatus()
    {
        ribbon.SetStatus(demo?Strings.Get("DemoStatus"):clipboard.Paused?Strings.Get("Paused"):Strings.Format("SavedStatus",store.Items.Count),clipboard.Paused);
    }
    private void SetStatus(string text) {if(ribbon!=null)ribbon.SetStatus(text,clipboard?.Paused??false);}
    private void ShowClipboardError(string text)
    {
        if(ribbon.IsVisible)SetStatus(text);
        else pendingClipboardError=text;
    }
    private void Notify(string text) {SetStatus(text);tray?.ShowBalloonTip(5000,Strings.Get("AppName"),text,Forms.ToolTipIcon.Info);}
    private void RequestExit() {if(ribbon!=null)ribbon.AllowClose=true;Shutdown();}
    private void TogglePause() {clipboard.Paused=!clipboard.Paused;Refresh();UpdateTrayTitle();}
    private void UpdateTrayTitle()
    {
        if(tray==null)return;
        var text=Strings.Get("AppName")+" · "+(clipboard.Paused?Strings.Get("Paused"):store.Settings.Hotkey);
        tray.Text=text.Length>63?text[..63]:text;
    }
    private void QueueSave() {if(demo)return;saveTimer.Stop();saveTimer.Start();}
    private bool Save()
    {
        if(demo)return true;
        try { store.Save();return true; }
        catch { Notify(Strings.Get("SaveFailed"));return false; }
    }
    private async Task PasteAsync(ClipItem item,bool plain)
    {
        if(demo) {SetStatus(Strings.Get("DemoPaste"));return;}
        if(pasteBusy)return;
        var target=pasteTarget;
        var targetProcess=Native.ProcessId(target);
        pasteBusy=true;
        try
        {
            if(await clipboard.WriteAsync(item,plain) is not { } writeSequence)return;
            if(!Native.IsWindow(target)||targetProcess==0||targetProcess==Environment.ProcessId) {SetStatus(Strings.Get("PasteManually"));return;}
            ribbon.DismissDrawer();
            await Task.Delay(170);
            if(Native.ProcessId(target)!=targetProcess || !Native.SetForegroundWindow(target)) {Notify(Strings.Get("PasteManually"));return;}
            for(var i=0;i<30 && (Native.IsDown(0x10)||Native.IsDown(0x11)||Native.IsDown(0x12)||Native.IsDown(0x0D));i++)await Task.Delay(20);
            if(!clipboard.IsWriteCurrent(writeSequence) || Native.GetForegroundWindow()!=target || Native.ProcessId(target)!=targetProcess)
            {Notify(Strings.Get("PasteStateChanged"));return;}
            if(Native.IsDown(0x10)||Native.IsDown(0x11)||Native.IsDown(0x12)||Native.IsDown(0x0D)) {Notify(Strings.Get("ReleaseKeys"));return;}
            if(Native.SendPaste()) {if(store.Settings.PasteSoundEnabled)sounds.Paste();}
            else Notify(Strings.Get("PasteRestricted"));
        }
        finally {pasteBusy=false;}
    }
    private async void OpenSettings()
    {
        if(!started || settingsOpen)return;
        settingsOpen=true;ribbon.DismissDrawer();
        var settings=new SettingsWindow(store.Settings);
        settings.ClearHistoryRequested+=()=>
        {
            clipboard.InvalidateCapture();store.Clear(true);Refresh();
            return Save();
        };
        settings.PreviewCopySoundRequested+=volume=>{sounds.SetVolume(volume);sounds.Copy();};
        settings.PreviewPasteSoundRequested+=volume=>{sounds.SetVolume(volume);sounds.Paste();};
        settings.AppearancePreviewRequested+=(theme,accent)=>ThemeManager.Apply(theme,accent);
        try
        {
            if(settings.ShowDialog()==true && settings.Result is { } next)
            {
                if(!demo && next.Hotkey!=store.Settings.Hotkey && bridge?.SetHotkey(next.Hotkey)==false)
                {next.Hotkey=store.Settings.Hotkey;Notify(Strings.Get("HotkeyKept"));}
                var startupChanged=next.StartAtLogin!=store.Settings.StartAtLogin;
                store.Settings=next;store.ApplyRetention();Save();
                if(!demo && !testSession && startupChanged)
                {
                    var warning=await StartupService.SetEnabledAsync(next.StartAtLogin);
                    if(warning!=null)Notify(warning);
                }
                Strings.SetLanguage(store.Settings.Language);
                RebuildTrayMenu();UpdateTrayTitle();
            }
        }
        finally {sounds.SetVolume(store.Settings.Volume);ThemeManager.Apply(store.Settings.Theme,store.Settings.Accent);settingsOpen=false;OpenRibbon();}
    }
    protected override void OnExit(ExitEventArgs e)
    {
        saveTimer.Stop();retentionTimer.Stop();if(started)Save();started=false;bridge?.Dispose();sounds.Dispose();
        if(tray!=null){tray.Visible=false;tray.Dispose();}
        ThemeManager.Shutdown();
        activationWait?.Unregister(null);activation?.Dispose();mutex?.Dispose();base.OnExit(e);
    }
}
