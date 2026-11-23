using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Views;

internal static class AnimationProbe
{
    internal static async Task<bool> Run(string output)
    {
        var version = typeof(RibbonWindow).Assembly.GetName().Version?.ToString();
        var window = new RibbonWindow { Title = "Vstavka animation check " + version };
        window.SetStatus("Animation check " + version + " · Synthetic data", false);
        var items = MakeItems();
        var cycles = new List<object>();
        int positionMessages = 0;
        HwndSource? source = null;
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source.AddHook((nint hwnd, int msg, nint wp, nint lp, ref bool handled) => { if (msg == 0x47) positionMessages++; return 0; });
        };
        bool allOpened = true, interruptedReopenVisible = false, immediateDismissHidden = false;
        string? error = null;
        try
        {
            for (int cycle = 0; cycle < 6; cycle++)
            {
                var frames = new List<Frame>();
                var clock = Stopwatch.StartNew();
                TimeSpan previousRender = TimeSpan.MinValue;
                var surface = window.FindName("AnimatedSurface") as UIElement;
                int positionsBefore = positionMessages;
                EventHandler onRender = (_, args) =>
                {
                    var rendering = (RenderingEventArgs)args;
                    if (rendering.RenderingTime == previousRender) return;
                    previousRender = rendering.RenderingTime;
                    double y = surface?.RenderTransform is TranslateTransform transform ? transform.Y : 0;
                    frames.Add(new Frame(clock.Elapsed.TotalMilliseconds, window.Top, y, surface?.Opacity ?? window.Opacity));
                };
                CompositionTarget.Rendering += onRender;
                try
                {
                    window.SetItems(items);
                    window.OpenDrawer();
                    double openCallMs = clock.Elapsed.TotalMilliseconds;
                    await Task.Delay(330);
                    allOpened &= window.IsVisible;
                    var visible = frames.Where(x => x.Opacity > 0.01).ToArray();
                    var gaps = frames.Zip(frames.Skip(1), (a, b) => b.Ms - a.Ms).Order().ToArray();
                    cycles.Add(new
                    {
                        cycle, openCallMs, frameCallbacks = frames.Count,
                        firstVisibleCallbackMs = visible.FirstOrDefault()?.Ms,
                        maxCallbackGapMs = gaps.LastOrDefault(),
                        p95CallbackGapMs = gaps.Length > 0 ? gaps[(int)((gaps.Length - 1) * .95)] : 0,
                        nativePositionMessages = positionMessages - positionsBefore,
                        distinctWindowTops = frames.Select(x => Math.Round(x.Top, 2)).Distinct().Count(),
                        distinctVisualPositions = visible.Select(x => Math.Round(x.Top + x.Offset, 2)).Distinct().Count(),
                        visibleAtEnd = window.IsVisible, frames
                    });
                }
                finally { CompositionTarget.Rendering -= onRender; }
                window.DismissDrawer(); await Task.Delay(170);
            }
            window.OpenDrawer(); await Task.Delay(45);
            window.DismissDrawer(); await Task.Delay(25);
            window.OpenDrawer(); await Task.Delay(350);
            interruptedReopenVisible = window.IsVisible;
            window.DismissDrawer(); await Task.Delay(170);
            window.OpenDrawer(); window.DismissDrawer(); await Task.Delay(300);
            immediateDismissHidden = !window.IsVisible;
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { window.AllowClose = true; window.Close(); }
        bool passed = error is null && allOpened && interruptedReopenVisible && immediateDismissHidden;
        var report = new
        {
            schema = "vstavka.performance.animation.v1", version = typeof(RibbonWindow).Assembly.GetName().Version?.ToString(),
            syntheticOnly = true, clipboardAccessed = false, keysInjected = false,
            animationEnabled = SystemParameters.ClientAreaAnimation, renderTier = RenderCapability.Tier >> 16,
            note = "WPF composition callbacks and HWND position messages; callbacks do not prove monitor-presented frames.",
            passed, interruptedReopenVisible, immediateDismissHidden, error, cycles
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }

    private sealed record Frame(double Ms, double Top, double Offset, double Opacity);

    private static ClipItem[] MakeItems()
    {
        const int width = 1920, height = 1080;
        var pixels = new byte[width * height * 4];
        var random = new Random(42); random.NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream(); encoder.Save(buffer); var image = buffer.ToArray();
        return Enumerable.Range(0, 500).Select(i => new ClipItem
        {
            Id = "animation-" + i, Kind = i < 6 ? ClipKind.Image : ClipKind.Text,
            ImageBytes = i < 6 ? image : null, Text = "Synthetic preview " + i + "\n" + new string('x', 4000),
            SourceApp = "Animation check", CapturedAt = DateTimeOffset.UnixEpoch.AddMinutes(-i)
        }).ToArray();
    }
}
