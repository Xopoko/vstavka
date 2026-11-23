using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace System.Windows
{
    // Only this in-memory boundary replaces Windows clipboard access. Never reads real user data.
    internal static class Clipboard
    {
        internal static IDataObject? Data;
        internal static BitmapSource? Image;
        internal static Action? ImageRead;
        internal static IDataObject? Written;
        internal static int BusyReads;
        internal static int BusyWrites;
        internal static int Writes;
        internal static int ImageReads;
        public static IDataObject? GetDataObject()
        {
            Vstavka.Services.Native.AssertSta();
            if(BusyReads-->0)throw new COMException("Synthetic clipboard contention");
            return Data;
        }
        public static BitmapSource? GetImage()
        {
            Vstavka.Services.Native.AssertSta();
            ImageReads++;
            ImageRead?.Invoke();
            return Image;
        }
        public static void SetDataObject(object data,bool copy)
        {
            Vstavka.Services.Native.AssertSta();
            if(BusyWrites-->0)throw new COMException("Synthetic clipboard contention");
            Written=(IDataObject)data;
            Writes++;
            Vstavka.Services.Native.Sequence++;
        }
        internal static void Reset()
        {
            Data=null; Image=null; ImageRead=null; Written=null;
            BusyReads=BusyWrites=Writes=ImageReads=0;
            Vstavka.Services.Native.Reset();
        }
    }
}

namespace Vstavka.Services
{
    internal static class Native
    {
        internal static int StaThread;
        internal static uint Sequence=1;
        internal static uint OwnerProcess=11;
        internal static string OwnerProcessName="SyntheticWriter";
        internal static readonly Dictionary<string,uint> Formats=new();
        internal static readonly HashSet<uint> Available=new();
        internal static nint HistoryValue;
        internal static void AssertSta()
        {
            if(Environment.CurrentManagedThreadId!=StaThread || Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)
                throw new InvalidOperationException("Clipboard API left its owning STA thread.");
        }
        internal static uint RegisterClipboardFormat(string name)
        {
            AssertSta();
            if(!Formats.TryGetValue(name,out var id))Formats[name]=id=(uint)(100+Formats.Count);
            return id;
        }
        internal static uint GetClipboardSequenceNumber(){ AssertSta(); return Sequence; }
        internal static int CountClipboardFormats(){ AssertSta(); return Clipboard.Data==null?0:1; }
        internal static nint GetClipboardOwner(){ AssertSta(); return 1; }
        internal static nint GetForegroundWindow(){ AssertSta(); return 2; }
        internal static uint ProcessId(nint window){ AssertSta(); return window==1?OwnerProcess:22; }
        internal static string ProcessName(uint processId){ AssertSta(); return processId==OwnerProcess?OwnerProcessName:"SyntheticEditor"; }
        internal static bool OpenClipboard(nint window){ AssertSta(); return true; }
        internal static bool CloseClipboard(){ AssertSta(); return true; }
        internal static bool IsClipboardFormatAvailable(uint format){ AssertSta(); return Available.Contains(format); }
        internal static nint GetClipboardData(uint format){ AssertSta(); return HistoryValue; }
        internal static nint GlobalLock(nint handle){ AssertSta(); return handle; }
        internal static bool GlobalUnlock(nint handle){ AssertSta(); return true; }
        internal static nuint GlobalSize(nint handle){ AssertSta(); return 4; }
        internal static void Reset()
        {
            AssertSta(); Sequence++; OwnerProcess=11; OwnerProcessName="SyntheticWriter"; Available.Clear();
            if(HistoryValue!=0)Marshal.FreeHGlobal(HistoryValue);
            HistoryValue=0;
        }
    }
}
