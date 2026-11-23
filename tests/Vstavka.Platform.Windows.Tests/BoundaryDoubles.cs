using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Vstavka.Platform.Windows;

internal sealed class MessageDouble : IWindowsMessages
{
    public event Action? ClipboardChanged;
    public event Action<int, uint>? HotkeyPressed;
    internal List<(int Id, uint Modifiers, uint Key)> Attempts { get; } = [];
    internal Dictionary<int, uint> Hotkeys { get; } = [];
    internal bool ListenerActive { get; private set; }
    internal bool RejectHotkey { get; set; }

    public void StartClipboardListener() => ListenerActive = true;
    public void StopClipboardListener() => ListenerActive = false;
    internal void Update() => ClipboardChanged?.Invoke();
    internal void Press(int id, uint? modifiers = null) =>
        HotkeyPressed?.Invoke(id, modifiers ?? (Hotkeys.GetValueOrDefault(id) & 0x000F));

    public bool RegisterHotkey(int id, uint modifiers, uint key)
    {
        Attempts.Add((id, modifiers, key));
        return !RejectHotkey && Hotkeys.TryAdd(id, modifiers);
    }

    public bool UnregisterHotkey(int id) => Hotkeys.Remove(id);
}

internal sealed class ClipboardDouble : IWindowsClipboard, IClipboardRead
{
    internal Dictionary<ClipboardFormat, byte[]> Data { get; private set; } = [];
    internal int BusyReads { get; set; }
    internal int BusyWrites { get; set; }
    internal int ReadAttempts { get; private set; }
    internal int WriteAttempts { get; private set; }
    internal int PayloadReads { get; private set; }
    internal Action? Rendering { get; set; }
    internal string[]? Files { get; set; }
    public uint Sequence { get; set; } = 1;
    public ClipboardOwner Owner { get; set; } = new("Synthetic.Writer", @"C:\Synthetic\Synthetic.Writer.exe");
    public string ForegroundProcessName { get; set; } = "Synthetic.Reader";

    internal void NewText(string text)
    {
        Data = new Dictionary<ClipboardFormat, byte[]> { [ClipboardFormat.Text] = Encoding.Unicode.GetBytes(text + '\0') };
        Sequence++;
    }

    public IClipboardRead? TryOpenRead()
    {
        ReadAttempts++;
        return BusyReads-- > 0 ? null : this;
    }

    public ClipboardWriteResult TryWrite(uint expectedSequence, IReadOnlyList<ClipboardData> data, out uint writtenSequence)
    {
        WriteAttempts++;
        writtenSequence = 0;
        if (BusyWrites-- > 0)
            return ClipboardWriteResult.Busy;
        if (Sequence != expectedSequence)
            return ClipboardWriteResult.Changed;
        Data = data.ToDictionary(value => value.Format, value => value.Bytes);
        writtenSequence = ++Sequence;
        return ClipboardWriteResult.Written;
    }

    public bool Has(ClipboardFormat format) => Data.ContainsKey(format);
    public nuint Size(ClipboardFormat format) => (nuint)(Data.GetValueOrDefault(format)?.Length ?? 0);
    public int? ReadFlag(ClipboardFormat format) => Data.GetValueOrDefault(format) is { Length: >= 4 } flag
        ? BinaryPrimitives.ReadInt32LittleEndian(flag) : null;

    public byte[]? ReadBytes(ClipboardFormat format, int maxBytes)
    {
        if (Data.GetValueOrDefault(format) is not { } bytes || bytes.Length > maxBytes)
            return null;
        PayloadReads++;
        Rendering?.Invoke();
        return bytes;
    }

    public string[]? ReadFiles(out bool tooMany)
    {
        PayloadReads++;
        tooMany = false;
        return Files;
    }

    public byte[]? ReadBitmap() => null;
    public void Dispose() { }
}

internal sealed class ImageDouble : IWindowsClipboardImages
{
    internal int Captures { get; private set; }

    public byte[] CapturePng(ClipboardFormat format, byte[] bytes)
    {
        Captures++;
        return bytes;
    }

    public byte[] CreateDibV5(byte[] png) => png;
}

internal sealed class IconDouble : IWindowsSourceIcons
{
    internal byte[] Png { get; } = [1, 2, 3];
    internal List<string> Paths { get; } = [];
    public byte[] GetPng(string executablePath)
    {
        Paths.Add(executablePath);
        return Png;
    }
}

internal sealed class PasteDouble : IWindowsPaste
{
    public nint ForegroundWindow { get; set; } = 1;
    public uint CurrentProcessId => 10;
    public bool ModifiersDown { get; set; }
    internal Dictionary<nint, uint> Processes { get; } = new() { [1] = 12, [2] = 10 };
    internal List<nint> Restored { get; } = [];
    internal bool CanRestore { get; set; } = true;
    internal bool CanSend { get; set; } = true;
    internal int SendAttempts { get; private set; }

    public uint ProcessId(nint window) => Processes.GetValueOrDefault(window);
    public bool RestoreForeground(nint window)
    {
        Restored.Add(window);
        if (CanRestore)
            ForegroundWindow = window;
        return CanRestore;
    }

    public bool SendPaste()
    {
        SendAttempts++;
        return CanSend;
    }
}

internal sealed class QueueContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();

    public override void Post(SendOrPostCallback callback, object? state) => queue.Enqueue((callback, state));

    internal void Run(Action action)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { action(); }
        finally { SetSynchronizationContext(previous); }
    }

    internal void Drain() => Run(() =>
    {
        while (queue.TryDequeue(out var work))
            work.Callback(work.State);
    });
}
