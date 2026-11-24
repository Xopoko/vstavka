using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Vstavka.Core;

namespace Vstavka.Platform.Windows;

internal interface IWindowsClipboardImages
{
    byte[]? CapturePng(ClipboardFormat format, byte[] bytes);
    byte[] CreateDibV5(byte[] png);
}

internal interface IWindowsSourceIcons
{
    byte[]? GetPng(string executablePath);
}

internal sealed class WindowsClipboard(
    IWindowsMessages messages,
    IWindowsClipboard backend,
    IWindowsClipboardImages images,
    IWindowsSourceIcons icons,
    Func<AppSettings> settings,
    Func<int, Task>? captureDelay = null,
    Action<int>? writeDelay = null) : IClipboardMonitor, IClipboardWriter
{
    private const int CaptureAttempts = 5;
    private const int MaxTextBuffer = 4 * 1024 * 1024;
    private const int MaxBinaryBuffer = 64 * 1024 * 1024;
    private static readonly ClipboardFormat[] ImageFormats = [ClipboardFormat.Png, ClipboardFormat.DibV5, ClipboardFormat.Dib];
    private static readonly PlatformNotice TooLarge = new("clipboard-item-too-large", "The clipboard item is too large.");
    private static readonly PlatformNotice TooManyFiles = new("clipboard-too-many-files", "The clipboard contains too many files.");
    private static readonly PlatformNotice ClipboardBusy = new("clipboard-busy", "The Windows clipboard is busy.");
    private static readonly PlatformNotice ReadFailed = new("clipboard-read-failed", "Could not read this clipboard item.");

    private readonly Func<int, Task> waitForCapture = captureDelay ?? (milliseconds => Task.Delay(milliseconds));
    private readonly Action<int> waitForWrite = writeDelay ?? Thread.Sleep;
    private readonly object state = new();
    private SynchronizationContext? context;
    private uint latestSequence;
    private uint lastWrittenSequence;
    private long writeGeneration;
    private bool listening;
    private bool disposed;

    public event Action<ClipboardCapture>? Captured;
    internal event Action? CopyDetected;
    internal event Action<PlatformNotice>? Notice;
    internal uint LastWrittenSequence => Volatile.Read(ref lastWrittenSequence);

    public void Start()
    {
        lock (state)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listening)
                return;
            context = SynchronizationContext.Current;
            latestSequence = backend.Sequence;
            messages.ClipboardChanged += OnClipboardChanged;
            try
            {
                messages.StartClipboardListener();
                listening = true;
            }
            catch
            {
                messages.ClipboardChanged -= OnClipboardChanged;
                throw;
            }
        }
    }

    private void OnClipboardChanged()
    {
        var sequence = backend.Sequence;
        lock (state)
        {
            if (!listening || sequence == 0 || sequence == latestSequence)
                return;
            latestSequence = sequence;
            if (sequence == LastWrittenSequence)
                return;
        }
        var capturedAt = DateTimeOffset.Now;
        Post(() =>
        {
            if (!IsCurrent(sequence))
                return;
            CopyDetected?.Invoke();
            var currentSettings = settings();
            _ = CaptureAsync(sequence, capturedAt, currentSettings, currentSettings.Copy());
        });
    }

    private async Task CaptureAsync(uint sequence, DateTimeOffset capturedAt, AppSettings currentSettings, AppSettings settingsSnapshot)
    {
        try
        {
            for (var attempt = 0; attempt < CaptureAttempts; attempt++)
            {
                await waitForCapture(attempt == 0 ? 30 : 50).ConfigureAwait(false);
                if (!IsCurrent(sequence))
                    return;

                CaptureSnapshot? snapshot;
                PlatformNotice? notice;
                using (var read = backend.TryOpenRead())
                {
                    if (read is null)
                        continue;
                    if (!IsCurrent(sequence))
                        return;
                    snapshot = ReadSnapshot(read, capturedAt, settingsSnapshot, out notice);

                    // Rendering a delayed clipboard format can advance the sequence while we hold the lock.
                    var renderedSequence = backend.Sequence;
                    lock (state)
                    {
                        if (!listening || latestSequence != sequence || renderedSequence == 0 ||
                            renderedSequence == LastWrittenSequence)
                            return;
                        latestSequence = sequence = renderedSequence;
                    }
                }
                if (notice is not null)
                    PublishNotice(sequence, notice);
                if (snapshot is not null)
                    EncodeAndPublish(snapshot, sequence, currentSettings);
                return;
            }
            PublishNotice(sequence, ClipboardBusy);
        }
        catch (Exception error) when (error is ExternalException or ArgumentException or
            InvalidOperationException or IOException or NotSupportedException or OutOfMemoryException)
        {
            PublishNotice(sequence, ReadFailed);
        }
    }

    private static CaptureSnapshot? ReadSnapshot(IClipboardRead read, DateTimeOffset capturedAt, AppSettings settings,
        out PlatformNotice? notice)
    {
        notice = null;
        if (read.Has(ClipboardFormat.Exclude) || read.Has(ClipboardFormat.ViewerIgnore) ||
            (read.Has(ClipboardFormat.IncludeInHistory) && read.ReadFlag(ClipboardFormat.IncludeInHistory) is null or 0))
            return null;
        var owner = read.Owner;
        var foreground = read.ForegroundProcessName;
        if (settings.IsAppExcluded(owner.Name) || settings.IsAppExcluded(foreground))
            return null;
        var item = new ClipItem { SourceApp = owner.Name, CapturedAt = capturedAt };
        if (read.Has(ClipboardFormat.Files))
        {
            var files = read.ReadFiles(MaxBinaryBuffer, out var tooLarge, out var tooMany);
            if (files is not { Length: > 0 })
            {
                notice = tooLarge ? TooLarge : tooMany ? TooManyFiles : ReadFailed;
                return null;
            }
            item.Kind = ClipKind.Files;
            item.Files = files;
            return new CaptureSnapshot(item, owner, foreground);
        }
        PlatformNotice? imageFailure = null;
        foreach (var format in ImageFormats)
        {
            if (!read.Has(format))
                continue;
            if (read.ReadBytes(format, MaxBinaryBuffer, out var tooLarge) is not { } bytes)
            {
                imageFailure ??= tooLarge ? TooLarge : ReadFailed;
                continue;
            }
            item.Kind = ClipKind.Image;
            return new CaptureSnapshot(item, owner, foreground, new ClipboardData(format, bytes));
        }
        if (read.ReadBitmap() is { } bitmap)
        {
            item.Kind = ClipKind.Image;
            return new CaptureSnapshot(item, owner, foreground, new ClipboardData(ClipboardFormat.Dib, bitmap));
        }
        if (!read.Has(ClipboardFormat.Text))
        {
            notice = imageFailure;
            return null;
        }
        if (read.ReadBytes(ClipboardFormat.Text, MaxTextBuffer, out var textTooLarge) is not { Length: >= sizeof(char) } textBytes)
        {
            notice = textTooLarge ? TooLarge : ReadFailed;
            return null;
        }
        var text = MemoryMarshal.Cast<byte, char>(textBytes);
        var terminator = text.IndexOf('\0');
        text = terminator < 0 ? text : text[..terminator];
        if (text.Length > HistoryStore.MaxTextLength)
        {
            notice = TooLarge;
            return null;
        }
        item.Text = new string(text);
        if (string.IsNullOrWhiteSpace(item.Text))
            return null;
        item.Kind = ClipItem.DetectTextKind(item.Text);
        return new CaptureSnapshot(item, owner, foreground);
    }

    private void EncodeAndPublish(CaptureSnapshot snapshot, uint sequence, AppSettings captureSettings)
    {
        if (!IsCurrent(sequence))
            return;
        if (snapshot.Image is { } image)
        {
            if (images.CapturePng(image.Format, image.Bytes) is not { } imagePng)
            {
                PublishNotice(sequence, ReadFailed);
                return;
            }
            snapshot.Item.ImageBytes = imagePng;
        }
        var source = string.IsNullOrWhiteSpace(snapshot.Owner.Name) ? null : new SourceApp(
            snapshot.Owner.Name, snapshot.Owner.Path, snapshot.Owner.Path is { } path ? icons.GetPng(path) : null);
        Post(() =>
        {
            if (!IsCurrent(sequence))
                return;
            var latestSettings = settings();
            if (ReferenceEquals(latestSettings, captureSettings) &&
                !latestSettings.IsAppExcluded(snapshot.Owner.Name) && !latestSettings.IsAppExcluded(snapshot.Foreground))
                Captured?.Invoke(new ClipboardCapture(snapshot.Item, source));
        });
    }

    private bool IsCurrent(uint sequence)
    {
        lock (state)
            return listening && sequence == latestSequence && sequence != LastWrittenSequence &&
                sequence == backend.Sequence;
    }

    private void PublishNotice(uint sequence, PlatformNotice notice) => Post(() =>
    {
        if (IsCurrent(sequence))
            Notice?.Invoke(notice);
    });

    private void Post(Action action)
    {
        if (context is { } target)
            target.Post(_ => action(), null);
        else
            ThreadPool.QueueUserWorkItem(_ => action());
    }

    public void Write(ClipItem item, bool plainText)
    {
        lock (state)
            ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(item);
        var generation = Interlocked.Increment(ref writeGeneration);
        Volatile.Write(ref lastWrittenSequence, 0);
        var initialSequence = backend.Sequence;
        var data = PrepareWrite(item, plainText);
        for (var attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            if (generation != Interlocked.Read(ref writeGeneration))
                break;
            var result = backend.TryWrite(initialSequence, data, out var writtenSequence);
            if (result == ClipboardWriteResult.Written)
            {
                Volatile.Write(ref lastWrittenSequence, writtenSequence);
                return;
            }
            if (result != ClipboardWriteResult.Busy || attempt == CaptureAttempts - 1)
                break;
            waitForWrite(45);
        }
        throw new InvalidOperationException("Could not write the item to the Windows clipboard.");
    }

    private List<ClipboardData> PrepareWrite(ClipItem item, bool plainText)
    {
        var data = new List<ClipboardData>
        {
            new(ClipboardFormat.IncludeInHistory, new byte[sizeof(int)]),
            new(ClipboardFormat.UploadToCloud, new byte[sizeof(int)])
        };
        switch (item.Kind)
        {
            case ClipKind.Image when plainText:
                throw new InvalidOperationException("An image has no plain text to copy.");
            case ClipKind.Image:
                if (item.ImageBytes is not { Length: > 0 and <= HistoryStore.MaxImageBytes } png)
                    throw new ArgumentException("The item has no supported image.", nameof(item));
                data.Add(new ClipboardData(ClipboardFormat.DibV5, images.CreateDibV5(png)));
                data.Add(new ClipboardData(ClipboardFormat.Png, png));
                break;
            case ClipKind.Files when !plainText:
                if (item.Files.Length is 0 or > HistoryStore.MaxFilesPerItem ||
                    item.Files.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > 32768 || path.Contains('\0')))
                    throw new ArgumentException("The clipboard file paths are invalid.", nameof(item));
                foreach (var path in item.Files)
                    if (!File.Exists(path) && !Directory.Exists(path))
                        throw new FileNotFoundException("A clipboard file was moved or deleted.", path);
                const int headerBytes = 20;
                var paths = Encoding.Unicode.GetBytes(string.Join('\0', item.Files) + "\0\0");
                var drop = new byte[headerBytes + paths.Length];
                BinaryPrimitives.WriteInt32LittleEndian(drop, headerBytes);
                BinaryPrimitives.WriteInt32LittleEndian(drop.AsSpan(16), 1); // DROPFILES.fWide
                paths.CopyTo(drop, headerBytes);
                data.Add(new ClipboardData(ClipboardFormat.Files, drop));
                break;
            default:
                var text = item.Kind == ClipKind.Files ? string.Join(Environment.NewLine, item.Files) : item.Text;
                if (text.Length > HistoryStore.MaxTextLength)
                    throw new ArgumentException("The clipboard text is too large.", nameof(item));
                data.Add(new ClipboardData(ClipboardFormat.Text, Encoding.Unicode.GetBytes(text + '\0')));
                break;
        }
        return data;
    }

    public void Dispose()
    {
        lock (state)
        {
            if (disposed)
                return;
            if (listening)
            {
                messages.StopClipboardListener();
                messages.ClipboardChanged -= OnClipboardChanged;
                listening = false;
            }
            disposed = true;
        }
    }

    private sealed record CaptureSnapshot(ClipItem Item, ClipboardOwner Owner, string Foreground, ClipboardData? Image = null);
}
