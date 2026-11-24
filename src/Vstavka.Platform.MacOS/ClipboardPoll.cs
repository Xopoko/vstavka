using Vstavka.Platform;

namespace Vstavka.Platform.MacOS;

internal sealed class ClipboardPoll(IPasteboard board, Func<FrontmostSource> getSource)
{
    private long lastCount;
    private long? ownCount;

    internal void Start() => lastCount = board.ChangeCount;

    internal void RecordOwnWrite(long count) => ownCount = count;

    internal ClipboardCapture? Check()
    {
        var count = board.ChangeCount;
        if (count == lastCount)
            return null;
        lastCount = count;

        var ownWrite = count == ownCount;
        ownCount = null;
        if (ownWrite || !ClipboardCapturePolicy.CanReadInBackground(board.Access))
            return null;

        var source = getSource();
        if (source.IsCurrentProcess)
            return null;
        var item = ClipboardCapturePolicy.Read(board, count);
        if (item is null || board.ChangeCount != count)
            return null;
        var app = source.Describe?.Invoke();
        if (board.ChangeCount != count)
            return null;
        item.SourceApp = app?.Name ?? string.Empty;
        return new ClipboardCapture(item, app);
    }
}

internal readonly record struct FrontmostSource(bool IsCurrentProcess, Func<SourceApp?>? Describe);

internal sealed class MacClipboardMonitor(ClipboardPoll poll) : IClipboardMonitor
{
    private Timer? timer;
    private bool disposed;
    private int tickPending;

    public event Action<ClipboardCapture>? Captured;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (timer is not null)
            return;
        var context = SynchronizationContext.Current ??
            throw new InvalidOperationException("Start the macOS clipboard monitor on the AppKit UI synchronization context.");
        using var pool = new AutoreleasePool();
        poll.Start();
        timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref tickPending, 1) == 0)
                context.Post(static state => ((MacClipboardMonitor)state!).Check(), this);
        }, null, 300, 300);
    }

    private void Check()
    {
        try
        {
            if (disposed)
                return;
            ClipboardCapture? capture;
            using (var pool = new AutoreleasePool())
                capture = poll.Check();
            if (capture is not null)
                Captured?.Invoke(capture);
        }
        finally
        {
            Interlocked.Exchange(ref tickPending, 0);
        }
    }

    public void Dispose()
    {
        disposed = true;
        timer?.Dispose();
        timer = null;
        Captured = null;
    }
}
