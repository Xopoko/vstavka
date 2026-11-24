using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vstavka.Core;

// In-memory synthetic data only: this benchmark never loads or saves history or uses the clipboard.
var store = new HistoryStore(Path.GetTempPath(), protector: null);
var timestamp = DateTimeOffset.UtcNow;
for (var index = 0; index < 490; index++)
    store.Add(new ClipItem { Text = $"Synthetic text {index:D4}: " + new string('x', 2048), CapturedAt = timestamp.AddHours(-2).AddSeconds(index) });
for (var index = 0; index < 10; index++)
{
    var bytes = new byte[HistoryStore.MaxImageBytes];
    bytes[0] = (byte)index;
    store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = bytes, CapturedAt = timestamp.AddMinutes(-1).AddSeconds(index) });
}
var sequence = 0;
var largeText = new ClipItem { Text = string.Concat(Enumerable.Repeat(new string('x', 99) + "\n", 10_000)) };
var shortText = new ClipItem { Text = "Short synthetic title" };
var fileStore = new HistoryStore(Path.GetTempPath(), protector: null);
for (var index = 0; index < 500; index++)
{
    var paths = Enumerable.Range(0, 100).Select(i => $@"C:\Synthetic\folder\document-{i:D3}.txt").ToArray();
    paths[^1] = $@"C:\Synthetic\folder\unique-{index:D3}.txt";
    fileStore.Add(new ClipItem { Kind = ClipKind.Files, Files = paths });
}
var duplicateFiles = new ClipItem { Kind = ClipKind.Files, Files = fileStore.Items[^1].Files.Select(x => x.ToLowerInvariant()).ToArray() };
var results = new[]
{
    Measure("Compare 100 file paths against 500 file entries", 40, () =>
    {
        duplicateFiles.CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        if (fileStore.Add(duplicateFiles) || fileStore.LastRejectionReason is not null)
            throw new InvalidOperationException("Duplicate file entry was not recognized.");
    }),
    Measure("Add 31-character text to 500 entries with 100 MiB images", 40, () =>
    {
        if (!store.Add(new ClipItem { Text = $"Synthetic new text {sequence++:D12}", CapturedAt = timestamp.AddSeconds(sequence) }))
            throw new InvalidOperationException("Synthetic entry was rejected.");
    }),
    Measure("Read title from 1,000,000-character multiline text", 100, () => GC.KeepAlive(largeText.Title)),
    Measure("Read title from short text", 10_000, () => GC.KeepAlive(shortText.Title))
};
var report = new
{
    schema = "vstavka.core-performance.v1",
    timestampUtc = DateTimeOffset.UtcNow,
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    syntheticOnly = true,
    historyItems = store.Items.Count,
    historyImageBytes = store.Items.Sum(x => x.ImageBytes?.LongLength ?? 0),
    results
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
var output = Array.IndexOf(args, "--output");
if (output >= 0 && output + 1 < args.Length)
{
    var path = Path.GetFullPath(args[output + 1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json);
}

static Measurement Measure(string name, int iterations, Action action)
{
    for (var index = 0; index < 5; index++) action();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var elapsed = new double[iterations];
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    for (var index = 0; index < iterations; index++)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        elapsed[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Array.Sort(elapsed);
    return new Measurement(name, iterations, elapsed[iterations / 2], elapsed[(int)Math.Ceiling(iterations * 0.95) - 1], allocated / iterations);
}

sealed record Measurement(string Name, int Iterations, double MedianMilliseconds, double P95Milliseconds, long AllocatedBytesPerOperation);
