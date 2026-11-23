using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Services;

internal sealed class ClipboardService
{
    private readonly Func<AppSettings> settings;
    private readonly Func<byte[],Task<BitmapSource>> decodeImage;
    private uint ownSequence;
    private uint requestedSequence;
    private long requestedCaptureGeneration;
    private uint lastFeedbackSequence;
    private uint? lastCapturedSequence;
    private bool capturing;
    private bool paused;
    private uint? testSourceProcessId;
    private long captureGeneration;
    private long writeGeneration;
    internal bool Paused
    {
        get=>paused;
        set { if(paused==value)return; paused=value; captureGeneration++; }
    }
    internal uint? TestSourceProcessId
    {
        get=>testSourceProcessId;
        set { if(testSourceProcessId==value)return; testSourceProcessId=value; captureGeneration++; }
    }
    internal event Action<ClipItem,uint>? Captured;
    internal event Action? CopyDetected;
    internal event Action<string>? Status;
    internal bool IsWriteCurrent(uint sequence)=>sequence!=0 && sequence==ownSequence && Native.GetClipboardSequenceNumber()==sequence;
    internal void InvalidateCapture()=>captureGeneration++;
    private static readonly uint ExcludeFormat=Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HistoryFormat=Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint ConfidentialFormat=Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
    internal ClipboardService(Func<AppSettings> settings,Func<byte[],Task<BitmapSource>>? imageDecoder=null)
    {
        this.settings=settings;
        decodeImage=imageDecoder??(bytes=>Task.Run(()=>DecodeImage(bytes)));
    }
    internal async void OnClipboardChanged()
    {
        if(TestSourceProcessId.HasValue && Native.ProcessId(Native.GetClipboardOwner())!=TestSourceProcessId.Value)return;
        requestedSequence=Native.GetClipboardSequenceNumber();
        requestedCaptureGeneration=captureGeneration;
        if(requestedSequence!=ownSequence && requestedSequence!=lastFeedbackSequence && Native.CountClipboardFormats()>0)
        {
            lastFeedbackSequence=requestedSequence;
            CopyDetected?.Invoke();
        }
        if(capturing)return;
        capturing=true;
        try
        {
            uint processed;
            do
            {
                processed=requestedSequence;
                var generation=requestedCaptureGeneration;
                if(!Paused && generation==captureGeneration && processed!=ownSequence && processed!=lastCapturedSequence)
                {
                    for(var attempt=0;attempt<5;attempt++)
                    {
                        await Task.Delay(attempt==0?30:50);
                        if(Paused || generation!=captureGeneration || Native.GetClipboardSequenceNumber()!=processed)break;
                        try
                        {
                            var sourceProcessId=Native.ProcessId(Native.GetClipboardOwner());
                            var item=await ReadCurrentAsync(processed,generation,sourceProcessId);
                            if(item!=null && !Paused && Native.GetClipboardSequenceNumber()==processed)
                            {
                                lastCapturedSequence=processed;
                                Captured?.Invoke(item,sourceProcessId);
                            }
                            break;
                        }
                        catch(COMException) { if(attempt==4)Status?.Invoke(Strings.Get("ClipboardBusy")); }
                        catch(InvalidOperationException) { Status?.Invoke(Strings.Get("CopyFailed")); break; }
                        catch(Exception ex) when(ex is OutOfMemoryException or IOException or ArgumentException or NotSupportedException) { Status?.Invoke(Strings.Get("UnsupportedFormat")); break; }
                    }
                }
            }while(processed!=requestedSequence);
        }
        finally { capturing=false; }
    }
    private async Task<ClipItem?> ReadCurrentAsync(uint sequence,long generation,uint sourceProcessId)
    {
        if(TestSourceProcessId.HasValue && sourceProcessId!=TestSourceProcessId.Value)return null;
        var captureSettings=settings();
        var source=Native.ProcessName(sourceProcessId);
        var foreground=Native.ProcessName(Native.ProcessId(Native.GetForegroundWindow()));
        if(captureSettings.IsAppExcluded(source)||captureSettings.IsAppExcluded(foreground))return null;
        if(!Native.OpenClipboard(0))throw new COMException("Clipboard busy");
        try
        {
            if(Native.IsClipboardFormatAvailable(ExcludeFormat)||Native.IsClipboardFormatAvailable(ConfidentialFormat))return null;
            if(Native.IsClipboardFormatAvailable(HistoryFormat))
            {
                var h=Native.GetClipboardData(HistoryFormat);
                var ptr=Native.GlobalLock(h);
                if(ptr==0)return null;
                try { if(Native.GlobalSize(h)<4 || Marshal.ReadInt32(ptr)==0)return null; }
                finally { Native.GlobalUnlock(h); }
            }
            foreach(var format in new uint[]{13,8,17,15})
            {
                if(!Native.IsClipboardFormatAvailable(format))continue;
                var size=Native.GlobalSize(Native.GetClipboardData(format));
                if(size>(format==13?4u*1024*1024:64u*1024*1024)) { Status?.Invoke(Strings.Get("ItemTooLarge")); return null; }
            }
        }
        finally { Native.CloseClipboard(); }
        var data=System.Windows.Clipboard.GetDataObject();
        if(data==null)return null;
        var item=new ClipItem { SourceApp=source,CapturedAt=DateTimeOffset.Now };
        if(data.GetDataPresent(DataFormats.FileDrop))
        {
            item.Kind=ClipKind.Files;
            item.Files=(string[]?)data.GetData(DataFormats.FileDrop)??Array.Empty<string>();
            if(item.Files.Length>HistoryStore.MaxFilesPerItem) {Status?.Invoke(Strings.Get("TooManyFiles"));return null;}
            if(item.Files.Length==0)return null;
        }
        else if(data.GetDataPresent(DataFormats.Bitmap))
        {
            var bitmap=System.Windows.Clipboard.GetImage();
            if(bitmap==null)return null;
            if((long)bitmap.PixelHeight*bitmap.PixelWidth>16_000_000) {Status?.Invoke(Strings.Get("ItemTooLarge"));return null;}
            // Only frozen pixel data crosses threads; clipboard access stays on its owning STA.
            if(!bitmap.IsFrozen)bitmap.Freeze();
            item.Kind=ClipKind.Image;
            item.ImageBytes=await Task.Run(()=>EncodeImage(bitmap));
        }
        else if(data.GetDataPresent(DataFormats.UnicodeText))
        {
            item.Text=(string?)data.GetData(DataFormats.UnicodeText)??"";
            if(item.Text.Length>HistoryStore.MaxTextLength) {Status?.Invoke(Strings.Get("ItemTooLarge"));return null;}
            if(string.IsNullOrWhiteSpace(item.Text))return null;
            item.Kind=ClipItem.DetectTextKind(item.Text);
        }
        else return null;
        // Encoding can outlive a new copy or a privacy-setting change. Never publish that old result.
        if(Paused || generation!=captureGeneration || sequence!=Native.GetClipboardSequenceNumber()
            || !ReferenceEquals(captureSettings,settings()) || captureSettings.IsAppExcluded(source) || captureSettings.IsAppExcluded(foreground)
            || (TestSourceProcessId.HasValue && Native.ProcessId(Native.GetClipboardOwner())!=TestSourceProcessId.Value))return null;
        if(item.Kind==ClipKind.Image && item.ImageBytes==null) {Status?.Invoke(Strings.Get("ItemTooLarge"));return null;}
        return item;
    }
    private static byte[]? EncodeImage(BitmapSource bitmap)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();encoder.Save(stream);
        return stream.Length>HistoryStore.MaxImageBytes?null:stream.ToArray();
    }
    private static BitmapSource DecodeImage(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
        return image;
    }
    internal async Task<uint?> WriteAsync(ClipItem item,bool plainText)
    {
        if(plainText && item.Kind==ClipKind.Image)
        {
            Status?.Invoke(Strings.Get("ImageHasNoText"));
            return null;
        }

        var generation=++writeGeneration;
        var sequence=Native.GetClipboardSequenceNumber();
        bool IsPendingWrite()=>generation==writeGeneration && sequence==Native.GetClipboardSequenceNumber();
        BitmapSource? replayImage=null;
        if(!plainText && item.Kind==ClipKind.Image && item.ImageBytes is {} bytes)
        {
            try { replayImage=await decodeImage(bytes); }
            catch(Exception ex) when(ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                if(IsPendingWrite())Status?.Invoke(Strings.Get("ClipboardWriteFailed"));
                return null;
            }
        }
        for(var attempt=0;attempt<5;attempt++)
        {
            // A later app write or external copy takes precedence over decoding and COM retries.
            if(!IsPendingWrite())return null;
            try
            {
                var data=new System.Windows.DataObject();
                if(plainText || item.Kind is ClipKind.Text or ClipKind.Link or ClipKind.Color)
                    data.SetData(DataFormats.UnicodeText,item.Kind==ClipKind.Files?string.Join(Environment.NewLine,item.Files):item.Text);
                else if(item.Kind==ClipKind.Files)
                {
                    if(item.Files.Any(x=>!File.Exists(x)&&!Directory.Exists(x))) { Status?.Invoke(Strings.Get("FileMissing"));return null; }
                    var paths=new StringCollection();paths.AddRange(item.Files);data.SetFileDropList(paths);
                }
                else if(item.Kind==ClipKind.Image && replayImage!=null)data.SetImage(replayImage);
                else break;
                // Explicitly prevent a second clipboard history or cloud upload on replay.
                data.SetData("CanIncludeInClipboardHistory",new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData("CanUploadToCloudClipboard",new MemoryStream(BitConverter.GetBytes(0)));
                System.Windows.Clipboard.SetDataObject(data,true);
                ownSequence=Native.GetClipboardSequenceNumber();
                return ownSequence;
            }
            catch(COMException) { if(attempt<4)await Task.Delay(45); }
            catch(Exception ex) when(ex is IOException or ArgumentException or NotSupportedException) { break; }
        }
        if(IsPendingWrite())Status?.Invoke(Strings.Get("ClipboardWriteFailed"));
        return null;
    }
}
