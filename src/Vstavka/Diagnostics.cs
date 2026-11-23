using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Presentation;
using Vstavka.Services;
using Vstavka.Views;

namespace Vstavka;

internal static class Diagnostics
{
    private static string Output(string[] args,string fallback) {var i=Array.IndexOf(args,"--output");return Path.GetFullPath(i>=0 && i+1<args.Length?args[i+1]:fallback);}
    internal static int Run(string[] args)
    {
        var checks=new List<object>();var failed=false;
        void Check(string name,Func<bool> run){try{var passed=run();checks.Add(new{name,passed});failed|=!passed;}catch(Exception e){checks.Add(new{name,passed=false,error=e.ToString()});failed=true;}}
        Check("native-input-x64-layout",()=>Marshal.SizeOf<Native.INPUT>()==(Environment.Is64BitProcess?40:28));
        Check("ui-search-filters-and-settings",()=>UiPreview.VerifyInteractions());
        Check("ui-collections-popup-menus-and-scrolling",()=>UiPreview.VerifyCollections());
        Check("card-refresh-selection-mutations-and-localization",()=>UiPreview.VerifyCardRefresh());
        Check("source-app-icon-fallback-and-refresh",()=>UiPreview.VerifySourceIcons());
        Check("source-app-icon-from-current-executable",()=>
        {
            var processId=(uint)Environment.ProcessId;
            var source=Native.ProcessName(processId);
            var icons=new SourceAppIcons();
            var request=icons.GetAsync(source,processId);
            var png=request.GetAwaiter().GetResult();
            var store=new HistoryStore(Path.GetTempPath());
            return png is not null && store.Add(new ClipItem{Text="Synthetic icon check",SourceApp=source}) &&
                store.SetSourceIcon(source,png) && ReferenceEquals(request,icons.GetAsync(source.ToUpperInvariant(),processId)) &&
                icons.GetAsync("missing-process",uint.MaxValue).GetAwaiter().GetResult() is null;
        });
        Check("retained-card-content-images-and-localization",()=>CardChecks.VerifyRetainedCards());
        Check("themes-and-all-language-layout",()=>UiPreview.VerifyAppearanceAndLayout());
        Check("closing-drawer-preserves-reopen-until-explicit-exit",()=>
        {
            var window=new RibbonWindow();
            var handle=new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            window.Close();
            var preserved=Native.IsWindow(handle);
            window.AllowClose=true;window.Close();
            return preserved && !Native.IsWindow(handle);
        });
        var output=Output(args,"app-checks.json");Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{schema="vstavka.checks.v1",passed=!failed,clipboardAccessed=false,keysInjected=false,checks},new JsonSerializerOptions{WriteIndented=true}));
        return failed?1:0;
    }
    internal static ClipItem[] SampleItems()
    {
        return new[]{
            new ClipItem{Kind=ClipKind.Text,Text="Release checklist\n\nReview translations\nCheck light and dark themes\nTest keyboard shortcuts\nCollect preview feedback",SourceApp="Notes",CapturedAt=DateTimeOffset.Now.AddMinutes(-2),IsPinned=true},
            new ClipItem{Kind=ClipKind.Link,Text="https://www.figma.com/community",SourceApp="Microsoft Edge",CapturedAt=DateTimeOffset.Now.AddMinutes(-4)},
            new ClipItem{Kind=ClipKind.Color,Text="#A78BFA",SourceApp="Figma",CapturedAt=DateTimeOffset.Now.AddMinutes(-6)},
            new ClipItem{Kind=ClipKind.Image,ImageBytes=SampleImage(),SourceApp="Photos",CapturedAt=DateTimeOffset.Now.AddMinutes(-8)},
            new ClipItem{Kind=ClipKind.Text,Text="const recent = clipboard\n  .filter(item => item.isPinned)\n  .map(item => item.text);",SourceApp="Visual Studio Code",CapturedAt=DateTimeOffset.Now.AddMinutes(-10)},
            new ClipItem{Kind=ClipKind.Files,Files=new[]{@"C:\Sample\Presentation.pdf",@"C:\Sample\Notes.txt"},SourceApp="File Explorer",CapturedAt=DateTimeOffset.Now.AddMinutes(-14)}
        };
    }
    private static byte[] SampleImage()
    {
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(221,206,246),Color.FromRgb(247,216,197),45),null,new Rect(0,0,700,400));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255,242,208)),null,new Point(490,110),55,55);
            var mountain=Geometry.Parse("M 0,400 L 0,280 140,190 260,275 400,140 700,360 700,400 Z");
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(162,143,192)),null,mountain);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(106,90,142)),null,Geometry.Parse("M 0,400 L 0,340 170,270 350,355 510,245 700,320 700,400 Z"));
        }
        var bitmap=new RenderTargetBitmap(700,400,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    internal static int Render(string[] args)
    {
        try
        {
            UiPreview.Render(Output(args,"preview.png"), SampleItems());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
