using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool live = args.Contains("--live");
        var paths = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (paths.Length != 1 || args.Length != (live ? 2 : 1))
        {
            Console.Error.WriteLine("Usage: Vstavka.Performance <report.json> [--live]");
            return 2;
        }
        var output = Path.GetFullPath(paths[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Strings.SetLanguage("en");
        if (live)
        {
            app.Startup += async (_, _) =>
            {
                int exitCode = 1;
                try { exitCode = await AnimationProbe.Run(output) ? 0 : 1; }
                catch (Exception error) { Console.Error.WriteLine(error); }
                app.Shutdown(exitCode);
            };
            return app.Run();
        }
        var results = new List<object>();
        var items = Enumerable.Range(0, 500).Select(i => new ClipItem
        {
            Id = "synthetic-" + i, Kind = ClipKind.Text,
            Text = $"Sample {i}: {(i % 2 == 0 ? "release" : "meeting")}\n" + new string('x', 4000),
            SourceApp = "Synthetic", CapturedAt = DateTimeOffset.UnixEpoch.AddMinutes(i)
        }).ToArray();
        var window = new RibbonWindow();
        var cards = (ListBox)window.FindName("Cards");
        var search = (TextBox)window.FindName("SearchBox");
        var root = (FrameworkElement)window.Content;
        void Layout() { root.Measure(new Size(1540, 338)); root.Arrange(new Rect(0, 0, 1540, 338)); root.UpdateLayout(); }
        Measure("first-populate-and-layout", 1, () => { window.SetItems(items); Layout(); });
        var originalCard = cards.Items[0];
        var originalContainer = cards.ItemContainerGenerator.ContainerFromIndex(0);
        Measure("unchanged-500-refresh-and-layout", 25, () => { window.SetItems(items); Layout(); });
        bool sameCard = ReferenceEquals(originalCard, cards.Items[0]);
        bool sameContainer = originalContainer is not null && ReferenceEquals(originalContainer, cards.ItemContainerGenerator.ContainerFromIndex(0));
        Measure("search-500-and-layout", 12, () => { search.Text = search.Text == "release" ? "meeting" : "release"; Layout(); });
        search.Text = "";
        Layout();
        var report = new
        {
            schema = "vstavka.performance.ui.v1", version = typeof(RibbonWindow).Assembly.GetName().Version?.ToString(),
            syntheticOnly = true, clipboardAccessed = false, windowsShown = false,
            note = "Offscreen UI-thread preparation/layout; not physical display frame timing.",
            renderTier = RenderCapability.Tier >> 16, size = "1540x338 DIP", itemCount = items.Length,
            sameCard, sameContainer, results
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        window.AllowClose = true; window.Close(); app.Shutdown();
        return 0;

        void Measure(string name, int iterations, Action action)
        {
            var samples = new List<double>(); long bytes = 0;
            for (int i = 0; i < iterations; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                action(); watch.Stop(); bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            results.Add(new { name, iterations, medianMs = samples[samples.Count / 2], maxMs = samples[^1], meanAllocatedBytes = bytes / iterations });
        }
    }
}
