using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Services;
using Clipboard=System.Windows.Clipboard;

internal static class Program
{
    private static readonly List<object> Results=[];
    private static BitmapSource sample=null!;
    private static byte[] encodedSample=null!;
    [STAThread]
    private static void Main(string[] args)
    {
        Native.StaThread=Environment.CurrentManagedThreadId;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try
            {
                sample=MakeImage(1920,1080);
                await Run("Synthetic 1080p PNG capture dispatcher latency",MeasureCapture);
                await Run("Newer clipboard content supersedes in-flight PNG",SupersededCapture);
                await Run("Pause then resume invalidates in-flight PNG",()=>InvalidatedCapture((service,replace)=>{service.Paused=true;service.Paused=false;}));
                await Run("Changed settings invalidate in-flight PNG",()=>InvalidatedCapture((service,replace)=>replace(new AppSettings())));
                await Run("New source exclusion invalidates in-flight PNG",()=>InvalidatedCapture((service,replace)=>currentSettings.ExcludedApps=["SyntheticWriter.exe"]));
                await Run("Changed test source invalidates in-flight PNG",()=>InvalidatedCapture((service,replace)=>Native.OwnerProcess=99));
                await Run("Privacy opt-out formats are never decoded or captured",PrivateFormats);
                await Run("A dotted process name respects the capture exclusion",ExcludedProcessName);
                await Run("Clipboard contention retries preserve one copy notification",Contention);
                await Run("Repeated notification for one sequence does not recapture PNG",RepeatedNotification);
                await Run("Replay decodes off dispatcher, retries and retains privacy flags",Replay);
                await Run("Newer write cancels an older image still decoding",()=>PendingWriteInvalidation(false));
                await Run("External copy cancels an older image still decoding",()=>PendingWriteInvalidation(true));
                await Run("Newer write cancels clipboard contention retry",()=>RetryInvalidation(false));
                await Run("External copy cancels clipboard contention retry",()=>RetryInvalidation(true));
                await Run("Clear during initial capture delay cannot repopulate history",ClearBeforeRead);
                await Run("Clear invalidates an image already encoding",()=>InvalidatedCapture((service,replace)=>service.InvalidateCapture()));
                await Run("Paste write identity never advances to a later replay",WriteIdentity);
                var json=JsonSerializer.Serialize(new {Results},new JsonSerializerOptions {WriteIndented=true});
                Console.WriteLine(json);
                if(args.Length==2 && args[0]=="--output")File.WriteAllText(args[1],json);
            }
            catch(Exception error){ Console.Error.WriteLine(error); Environment.ExitCode=1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run();
    }
    private static async Task Run(string name,Func<Task<object>> test)
    {
        try { Results.Add(new {Name=name,Passed=true,Evidence=await test()}); }
        catch(Exception error){ Results.Add(new {Name=name,Passed=false,Error=error.ToString()}); Environment.ExitCode=1; }
    }
    private static async Task<object> MeasureCapture()
    {
        var samples=new List<double>();
        var totals=new List<double>();
        var bytes=0;
        for(var attempt=0;attempt<4;attempt++)
        {
            Clipboard.Reset(); SetImage(sample);
            var settings=new AppSettings();
            var service=new ClipboardService(()=>settings);
            var capture=new TaskCompletionSource<(ClipItem Item,uint ProcessId)>();
            var dispatch=new TaskCompletionSource<double>();
            Clipboard.ImageRead=()=>
            {
                var watch=Stopwatch.StartNew();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input,()=>dispatch.TrySetResult(watch.Elapsed.TotalMilliseconds));
            };
            service.Captured+=(item,processId)=>capture.TrySetResult((item,processId));
            var total=Stopwatch.StartNew(); service.OnClipboardChanged();
            var (item,processId)=await capture.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var latency=await dispatch.Task;
            Check(processId==Native.OwnerProcess,"Capture reports the synthetic clipboard owner PID.");
            Check(item.Kind==ClipKind.Image && item.ImageBytes is {Length:>0},"Image capture is complete.");
            bytes=item.ImageBytes!.Length;
            encodedSample=item.ImageBytes;
            if(attempt>0){ samples.Add(latency); totals.Add(total.Elapsed.TotalMilliseconds); }
        }
        return new {Width=sample.PixelWidth,Height=sample.PixelHeight,EncodedBytes=bytes,InputDispatchDelayMs=samples,TotalCaptureMs=totals};
    }
    private static AppSettings currentSettings=null!;
    private static async Task<object> SupersededCapture()
    {
        Clipboard.Reset(); SetImage(sample); currentSettings=new AppSettings();
        var service=new ClipboardService(()=>currentSettings);
        var items=new List<ClipItem>(); var sounds=0;
        service.Captured+=(item,_)=>items.Add(item); service.CopyDetected+=()=>sounds++;
        Clipboard.ImageRead=()=>Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input,()=>
        {
            var data=new DataObject(); data.SetData(DataFormats.UnicodeText,"newer synthetic text");
            Clipboard.Data=data; Native.Sequence++; service.OnClipboardChanged();
        });
        service.OnClipboardChanged(); await AwaitIdle(service);
        Check(items.Count==1 && items[0].Text=="newer synthetic text","Only the newer sequence may be retained.");
        Check(sounds==2,"Each independent copy is still announced immediately.");
        return new {Captured=items.Count,CopyNotifications=sounds};
    }
    private static async Task<object> InvalidatedCapture(Action<ClipboardService,Action<AppSettings>> mutation)
    {
        Clipboard.Reset(); SetImage(sample); currentSettings=new AppSettings();
        var service=new ClipboardService(()=>currentSettings){TestSourceProcessId=11};
        var captures=0; var sounds=0;
        service.Captured+=(_,_)=>captures++; service.CopyDetected+=()=>sounds++;
        Clipboard.ImageRead=()=>Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input,()=>mutation(service,value=>currentSettings=value));
        service.OnClipboardChanged(); await AwaitIdle(service);
        Check(captures==0,"In-flight image must be discarded after the relevant state changes.");
        Check(sounds==1,"Copy notification remains independent of history capture.");
        return new {Captured=captures,CopyNotifications=sounds};
    }
    private static async Task<object> PrivateFormats()
    {
        foreach(var format in new[]{"ExcludeClipboardContentFromMonitorProcessing","Clipboard Viewer Ignore","CanIncludeInClipboardHistory"})
        {
            Clipboard.Reset(); SetImage(sample); currentSettings=new AppSettings();
            var service=new ClipboardService(()=>currentSettings); var captures=0; var sounds=0;
            Native.Available.Add(Native.Formats[format]);
            if(format=="CanIncludeInClipboardHistory")
            {
                Native.HistoryValue=Marshal.AllocHGlobal(4);Marshal.WriteInt32(Native.HistoryValue,0);
            }
            service.Captured+=(_,_)=>captures++; service.CopyDetected+=()=>sounds++;
            service.OnClipboardChanged(); await AwaitIdle(service);
            Check(captures==0 && Clipboard.ImageReads==0,"Opt-out format prevents image retrieval and persistence.");
            Check(sounds==1,"Privacy opt-out still announces that copying occurred.");
        }
        return new {Formats=3};
    }
    private static async Task<object> Contention()
    {
        Clipboard.Reset(); currentSettings=new AppSettings();
        var data=new DataObject(); data.SetData(DataFormats.UnicodeText,"contention test"); Clipboard.Data=data; Clipboard.BusyReads=2;
        var service=new ClipboardService(()=>currentSettings); var items=new List<ClipItem>(); var sounds=0; var errors=0;
        service.Captured+=(item,_)=>items.Add(item); service.CopyDetected+=()=>sounds++;service.Status+=_=>errors++;
        service.OnClipboardChanged(); await AwaitIdle(service);
        Check(items.Count==1 && items[0].Text=="contention test" && sounds==1 && errors==0,"Busy reads retry without losing text or duplicating feedback.");
        return new {Captured=items.Count,CopyNotifications=sounds};
    }
    private static async Task<object> ExcludedProcessName()
    {
        Clipboard.Reset(); SetImage(sample);
        Native.OwnerProcessName="Synthetic.Writer";
        var settings=new AppSettings { ExcludedApps=["synthetic.writer"] };
        var service=new ClipboardService(()=>settings);
        var captures=0;
        service.Captured+=(_,_)=>captures++;
        service.OnClipboardChanged(); await AwaitIdle(service);
        Check(captures==0 && Clipboard.ImageReads==0,"The full process name is matched before reading clipboard pixels.");
        return new {Captured=captures,ImageReads=Clipboard.ImageReads};
    }
    private static async Task<object> RepeatedNotification()
    {
        Clipboard.Reset(); SetImage(sample);
        var settings = new AppSettings();
        var service = new ClipboardService(() => settings);
        var captures = 0; var sounds = 0;
        service.Captured += (_, _) => captures++;
        service.CopyDetected += () => sounds++;
        service.OnClipboardChanged(); await AwaitIdle(service);
        service.OnClipboardChanged(); await AwaitIdle(service);
        Check(captures == 1 && sounds == 1 && Clipboard.ImageReads == 1,
            "Repeated notification must not read and encode the same clipboard image twice.");
        Native.Sequence++;
        service.OnClipboardChanged(); await AwaitIdle(service);
        Check(captures == 2 && sounds == 2 && Clipboard.ImageReads == 2,
            "An actual new copy of identical content must still be captured.");
        return new { Captured = captures, CopyNotifications = sounds, ImageReads = Clipboard.ImageReads };
    }

    private static async Task<object> Replay()
    {
        Clipboard.Reset(); currentSettings=new AppSettings();
        var service=new ClipboardService(()=>currentSettings);
        Clipboard.BusyWrites=2;
        var watch=Stopwatch.StartNew();
        var dispatched=new TaskCompletionSource<double>();
        _=Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input,()=>dispatched.TrySetResult(watch.Elapsed.TotalMilliseconds));
        var imageItem=new ClipItem{Kind=ClipKind.Image,ImageBytes=encodedSample};
        var sequence=await service.WriteAsync(imageItem,false);
        var inputDelay=await dispatched.Task;
        Check(sequence.HasValue && service.IsWriteCurrent(sequence.Value) && Clipboard.Writes==1,"Replay returns the sequence of its successful write after retrying.");
        var written=Clipboard.Written!;
        var image=(BitmapSource)written.GetData(DataFormats.Bitmap);
        Check(image.IsFrozen && image.PixelWidth==sample.PixelWidth && image.PixelHeight==sample.PixelHeight,"Replay image is fully loaded and frozen before returning to STA.");
        var pixel=new byte[4]; image.CopyPixels(new Int32Rect(132,231,1,1),pixel,4,0);
        var expected=new byte[4]; sample.CopyPixels(new Int32Rect(132,231,1,1),expected,4,0);
        Check(pixel.SequenceEqual(expected),"PNG roundtrip preserves pixel content.");
        foreach(var format in new[]{"CanIncludeInClipboardHistory","CanUploadToCloudClipboard"})
        {
            var flag=(MemoryStream)written.GetData(format);
            Check(BitConverter.ToInt32(flag.ToArray())==0,"Replay retains history and cloud opt-out flags.");
        }
        var sounds=0; service.CopyDetected+=()=>sounds++; service.OnClipboardChanged(); await AwaitIdle(service);
        Check(sounds==0,"Own replay never sounds as a fresh copy.");
        string? status=null; service.Status+=message=>status=message;
        Check(await service.WriteAsync(imageItem,true) is null && Clipboard.Written==written && status==Strings.Get("ImageHasNoText"),
            "Plain text replay of an image keeps the clipboard intact and explains the failure.");
        Clipboard.Written=null;
        Check(await service.WriteAsync(new ClipItem{Kind=ClipKind.Image,ImageBytes=[1,2,3]},false) is null && Clipboard.Written==null,"Invalid image never replaces the clipboard.");
        Check(await service.WriteAsync(new ClipItem{Kind=ClipKind.Text,Text="unchanged text"},false) is not null,"Text replay still works after decode failure.");
        return new {InputDispatchDelayMs=inputDelay,PixelRoundtrip=true,PrivacyFlags=2};
    }
    private static async Task AwaitIdle(ClipboardService service)
    {
        var capturing=typeof(ClipboardService).GetField("capturing",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var watch=Stopwatch.StartNew();
        while((bool)capturing.GetValue(service)!)
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException("Synthetic capture did not finish.");
            await Task.Delay(5);
        }
    }
    private static async Task<object> PendingWriteInvalidation(bool external)
    {
        Clipboard.Reset(); currentSettings=new AppSettings();
        var decoder=new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service=new ClipboardService(()=>currentSettings,_=>decoder.Task); var errors=0; service.Status+=_=>errors++;
        var older=service.WriteAsync(new ClipItem{Kind=ClipKind.Image,ImageBytes=encodedSample},false);
        Check(!older.IsCompleted,"The controlled decoder must be pending before the newer action.");
        await SupersedeWrite(service,external);
        decoder.SetResult(sample);
        Check(await older is null,"The older image may not overwrite newer clipboard intent.");
        Check(Clipboard.Written!.GetData(DataFormats.UnicodeText) is "newer write" && Clipboard.Writes==(external?0:1),"The newer clipboard payload must remain intact.");
        Check(errors==0,"Superseding user intent is not a write failure.");
        return new {Canceled=true,External=external,Writes=Clipboard.Writes};
    }
    private static async Task<object> RetryInvalidation(bool external)
    {
        Clipboard.Reset(); currentSettings=new AppSettings(); Clipboard.BusyWrites=1;
        var service=new ClipboardService(()=>currentSettings); var errors=0; service.Status+=_=>errors++;
        var older=service.WriteAsync(new ClipItem{Kind=ClipKind.Text,Text="older retry"},false);
        Check(!older.IsCompleted,"The first attempt must yield after controlled COM contention.");
        await SupersedeWrite(service,external);
        Check(await older is null,"The older COM retry may not overwrite newer clipboard intent.");
        Check(Clipboard.Written!.GetData(DataFormats.UnicodeText) is "newer write" && Clipboard.Writes==(external?0:1),"Canceled retry must not perform another write.");
        Check(errors==0,"Canceled retry is not a write failure.");
        return new {Canceled=true,External=external,Writes=Clipboard.Writes};
    }
    private static async Task SupersedeWrite(ClipboardService service,bool external)
    {
        if(external)
        {
            var data=new DataObject(); data.SetData(DataFormats.UnicodeText,"newer write");
            Clipboard.Data=data; Clipboard.Written=data; Native.Sequence++;
        }
        else Check(await service.WriteAsync(new ClipItem{Kind=ClipKind.Text,Text="newer write"},false) is not null,"Newer write must succeed.");
    }
    private static async Task<object> ClearBeforeRead()
    {
        Clipboard.Reset(); SetImage(sample); currentSettings=new AppSettings();
        var service=new ClipboardService(()=>currentSettings); var items=new List<ClipItem>(); service.Captured+=(item,_)=>items.Add(item);
        service.OnClipboardChanged(); service.InvalidateCapture(); await AwaitIdle(service);
        Check(items.Count==0 && Clipboard.ImageReads==0,"Clear must invalidate the event before its initial clipboard read.");
        Native.Sequence++; service.OnClipboardChanged(); await AwaitIdle(service);
        Check(items.Count==1 && Clipboard.ImageReads==1,"An actual new copy after clearing must still be retained.");
        return new {OldCaptureCanceled=true,NewCapturePreserved=true};
    }
    private static async Task<object> WriteIdentity()
    {
        Clipboard.Reset(); currentSettings=new AppSettings(); var service=new ClipboardService(()=>currentSettings);
        Check(!service.IsWriteCurrent(0),"An empty write token is never current.");
        var first=await service.WriteAsync(new ClipItem{Kind=ClipKind.Text,Text="first"},false) ?? throw new InvalidOperationException("First replay failed.");
        Check(service.IsWriteCurrent(first),"The successful replay initially owns its clipboard sequence.");
        var second=await service.WriteAsync(new ClipItem{Kind=ClipKind.Text,Text="second"},false) ?? throw new InvalidOperationException("Second replay failed.");
        Check(!service.IsWriteCurrent(first) && service.IsWriteCurrent(second),"A pending paste must keep the sequence of its own replay.");
        Native.Sequence++;
        Check(!service.IsWriteCurrent(second),"External clipboard change invalidates the exact replay token.");
        return new {SeparateReplayTokens=true,ExternalChangeInvalidates=true};
    }
    private static void SetImage(BitmapSource image)
    {
        var data=new DataObject(); data.SetData(DataFormats.Bitmap,image);
        Clipboard.Data=data; Clipboard.Image=image;
    }
    private static BitmapSource MakeImage(int width,int height)
    {
        var pixels=new byte[width*height*4]; new Random(739).NextBytes(pixels);
        for(var offset=3;offset<pixels.Length;offset+=4)pixels[offset]=255;
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        return image;
    }
    private static void Check(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }
}
