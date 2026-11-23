using System.Buffers.Binary;
using System.Text;
using Vstavka.Core;
using Vstavka.Platform;
using Vstavka.Platform.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Run("Clipboard privacy, formats, and source attribution", CaptureFormats);
        Run("Clipboard retries and suppresses own writes", ClipboardRetryAndSuppression);
        Run("Hotkey mappings and safe replacement", Hotkeys);
        Run("Paste restores the target and handles failure", Paste);
        Run("Preview isolation and Windows image conversion", IsolationAndCodecs);
    }

    private static void CaptureFormats()
    {
        var messages = new MessageDouble();
        var native = new ClipboardDouble();
        var icons = new IconDouble();
        var images = new ImageDouble();
        var settings = new AppSettings();
        using var monitor = new WindowsClipboard(messages, native, images, icons, () => settings, _ => Task.CompletedTask);
        var ui = new QueueContext();
        var captures = new List<ClipboardCapture>();
        monitor.Captured += capture =>
        {
            Check(ReferenceEquals(SynchronizationContext.Current, ui), "Capture is delivered on the Start context.");
            captures.Add(capture);
        };
        ui.Run(monitor.Start);

        foreach (var (format, data) in new (ClipboardFormat, byte[])[]
        {
            (ClipboardFormat.Exclude, []), (ClipboardFormat.ViewerIgnore, []),
            (ClipboardFormat.IncludeInHistory, [0, 0, 0, 0]), (ClipboardFormat.IncludeInHistory, [1])
        })
        {
            native.NewText("private");
            native.Data[ClipboardFormat.Png] = [32];
            native.Data[format] = data;
            messages.Update();
            ui.Drain();
        }
        Check(captures.Count == 0 && native.PayloadReads == 0 && icons.Paths.Count == 0,
            "Privacy formats must skip data and icon retrieval, including a malformed opt-in flag.");

        settings.ExcludedApps = ["Synthetic.Reader.exe"];
        native.NewText("excluded by foreground process");
        messages.Update();
        ui.Drain();
        Check(captures.Count == 0 && native.PayloadReads == 0, "Foreground exclusion is checked before payload reads.");
        settings.ExcludedApps = [];

        native.NewText("https://example.com/");
        native.Data[ClipboardFormat.IncludeInHistory] = [1, 0, 0, 0];
        messages.Update();
        Check(captures.Count == 0, "A native notification does not invoke the callback directly.");
        ui.Drain();
        var text = captures.Single();
        Check(text.Item is { Kind: ClipKind.Link, Text: "https://example.com/", SourceApp: "Synthetic.Writer" } &&
            text.Source?.Name == native.Owner.Name && text.Source.Id == native.Owner.Path &&
            text.Source.IconPng == icons.Png && icons.Paths.Single() == native.Owner.Path,
            "The owner, executable and icon are mapped to the capture.");

        native.NewText("fallback text");
        native.Data[ClipboardFormat.Png] = [21];
        native.Data[ClipboardFormat.Files] = [1];
        native.Files = [@"C:\Synthetic\test.txt"];
        messages.Update();
        ui.Drain();
        Check(captures[^1].Item is { Kind: ClipKind.Files } && captures[^1].Item.Files.SequenceEqual(native.Files) &&
            images.Captures == 0, "A file drop takes precedence over image and text.");
        native.NewText("fallback text");
        native.Data[ClipboardFormat.DibV5] = [37];
        messages.Update();
        ui.Drain();
        Check(captures[^1].Item is { Kind: ClipKind.Image } && captures[^1].Item.ImageBytes!.SequenceEqual(new byte[] { 37 }) &&
            images.Captures == 1, "DIB images are converted to PNG before capture.");

        native.NewText(new string('a', HistoryStore.MaxTextLength + 1));
        messages.Update();
        ui.Drain();
        Check(captures.Count == 3, "Text over the history limit is skipped.");
    }

    private static void ClipboardRetryAndSuppression()
    {
        var messages = new MessageDouble();
        var native = new ClipboardDouble { BusyReads = 2, BusyWrites = 2 };
        var settings = new AppSettings();
        Action? onWriteDelay = null;
        using var clipboard = new WindowsClipboard(messages, native, new ImageDouble(), new IconDouble(), () => settings,
            _ => Task.CompletedTask, _ => onWriteDelay?.Invoke());
        var ui = new QueueContext();
        var captures = new List<string>();
        clipboard.Captured += capture => captures.Add(capture.Item.Text);
        ui.Run(clipboard.Start);
        native.NewText("external");
        native.Rendering = () => { native.Sequence++; native.Rendering = null; };
        messages.Update();
        ui.Drain();
        messages.Update();
        ui.Drain();
        Check(native.ReadAttempts == 3 && captures.SequenceEqual(["external"]),
            "Busy reads retry, and delayed rendering or duplicate notifications do not duplicate capture.");

        clipboard.Write(new ClipItem { Text = "replay" }, false);
        Check(native.WriteAttempts == 3 && clipboard.LastWrittenSequence == native.Sequence &&
            Encoding.Unicode.GetString(native.Data[ClipboardFormat.Text]) == "replay\0" &&
            native.ReadFlag(ClipboardFormat.IncludeInHistory) == 0 && native.ReadFlag(ClipboardFormat.UploadToCloud) == 0,
            "Busy writes retry and publish text with both privacy flags.");
        messages.Update();
        ui.Drain();
        Check(native.ReadAttempts == 3 && captures.Count == 1, "A replay is suppressed before opening the clipboard.");

        clipboard.SuppressNextChange();
        native.NewText("explicitly suppressed");
        messages.Update();
        ui.Drain();
        native.NewText("next external");
        messages.Update();
        ui.Drain();
        Check(captures.SequenceEqual(["external", "next external"]), "Explicit suppression is consumed once.");

        clipboard.SuppressNextChange();
        native.BusyWrites = 1;
        onWriteDelay = () => native.NewText("newer copy while writing");
        var failed = false;
        try { clipboard.Write(new ClipItem { Text = "old pending replay" }, false); }
        catch (InvalidOperationException) { failed = true; }
        messages.Update();
        ui.Drain();
        Check(failed && clipboard.LastWrittenSequence == 0 && captures[^1] == "newer copy while writing",
            "A newer copy supersedes a pending write, and a failed write does not suppress the copy.");

        var file = new ClipItem { Kind = ClipKind.Files, Files = [AppContext.BaseDirectory] };
        clipboard.Write(file, false);
        var fileDrop = native.Data[ClipboardFormat.Files];
        Check(BinaryPrimitives.ReadInt32LittleEndian(fileDrop) == 20 &&
            BinaryPrimitives.ReadInt32LittleEndian(fileDrop.AsSpan(16)) == 1 &&
            Encoding.Unicode.GetString(fileDrop[20..]) == AppContext.BaseDirectory + "\0\0",
            "Formatted file replay uses a Unicode DROPFILES payload.");
        clipboard.Write(file, true);
        Check(Encoding.Unicode.GetString(native.Data[ClipboardFormat.Text]) == AppContext.BaseDirectory + '\0',
            "Plain text file replay writes paths as text.");
        clipboard.Dispose();
        Check(!messages.ListenerActive, "Disposal stops the listener.");
    }

    private static void Hotkeys()
    {
        var messages = new MessageDouble();
        using var hotkey = new WindowsHotkey(messages);
        var original = new QueueContext();
        var replacement = new QueueContext();
        var delivered = new List<SynchronizationContext?>();
        hotkey.Pressed += () => delivered.Add(SynchronizationContext.Current);
        original.Run(() => Check(hotkey.TryRegister("Ctrl+Shift+V"), "The default shortcut registers."));

        messages.RejectHotkey = true;
        replacement.Run(() => Check(!hotkey.TryRegister("Ctrl+Alt+V"), "A taken shortcut is rejected."));
        Check(messages.Hotkeys.Keys.SequenceEqual([1]), "The original registration remains after failure.");
        messages.Press(1);
        messages.Press(2);
        replacement.Drain();
        Check(delivered.Count == 0, "A failed replacement does not change the event context.");
        original.Drain();
        Check(delivered.Count == 1 && delivered[0] == original, "The original shortcut still fires on its context.");

        messages.RejectHotkey = false;
        replacement.Run(() => Check(hotkey.TryRegister("Ctrl+Alt+V"), "The next shortcut registers."));
        messages.Press(2);
        replacement.Run(() => Check(hotkey.TryRegister("Alt+V") && hotkey.TryRegister(" alt+v "),
            "The final shortcut registers, and registering the same gesture is idempotent."));
        Check(!hotkey.TryRegister("Ctrl+V") && messages.Hotkeys.Keys.SequenceEqual([1]),
            "Unsupported gestures leave the last registration intact.");
        messages.Press(1, 0x0006);
        messages.Press(1);
        replacement.Drain();
        Check(delivered.Count == 2 && delivered[^1] == replacement, "A retired registration cannot dispatch a queued press.");
        Check(messages.Attempts.SequenceEqual(new (int, uint, uint)[]
        {
            (1, 0x4006, 0x56), (2, 0x4003, 0x56), (2, 0x4003, 0x56), (1, 0x4001, 0x56)
        }), "All supported gestures use V and MOD_NOREPEAT.");
        hotkey.Dispose();
        Check(messages.Hotkeys.Count == 0, "Disposal unregisters the active shortcut.");
    }

    private static void Paste()
    {
        var native = new PasteDouble();
        uint sequence = 7;
        Action? onDelay = null;
        var paste = new WindowsPaste(native, () => sequence, () => 7, _ => { onDelay?.Invoke(); return Task.CompletedTask; });
        paste.CaptureTarget();
        native.ForegroundWindow = 2;
        paste.CaptureTarget();
        native.ModifiersDown = true;
        var delays = 0;
        onDelay = () => { if (++delays == 2) native.ModifiersDown = false; };
        Check(paste.PasteAsync().GetAwaiter().GetResult() && native.Restored.SequenceEqual(new nint[] { 1 }) &&
            native.SendAttempts == 1 && delays == 2, "Paste restores the recorded external target and waits for released keys.");

        onDelay = null;
        native.CanRestore = false;
        Check(!paste.PasteAsync().GetAwaiter().GetResult() && native.SendAttempts == 1,
            "Failed foreground restoration does not send keys.");
        native.CanRestore = true;
        native.CanSend = false;
        Check(!paste.PasteAsync().GetAwaiter().GetResult() && native.SendAttempts == 2,
            "Blocked input reports a failed paste.");

        onDelay = () => sequence++;
        var restoreAttempts = native.Restored.Count;
        Check(!paste.PasteAsync().GetAwaiter().GetResult() && native.Restored.Count == restoreAttempts &&
            native.SendAttempts == 2, "Changing the clipboard before paste cancels the old request.");
        sequence = 7;
        native.Processes[1] = 13;
        Check(!paste.PasteAsync().GetAwaiter().GetResult() && native.SendAttempts == 2,
            "A recycled window handle cannot receive the paste.");
    }

    private static void IsolationAndCodecs()
    {
        var root = Path.GetTempPath();
        const string sid = "S-1-5-21-1";
        Check(WindowsPlatformServices.PreviewDataDirectory(root) == Path.Combine(root, "Vstavka.Preview") &&
            WindowsPlatformServices.PreviewInstanceName(sid) == $@"Local\Vstavka.Preview.{sid}" &&
            WindowsPlatformServices.PreviewInstanceName(sid) != $@"Local\Vstavka.{sid}",
            "The preview has separate history and single-instance names.");
        Check(WindowsPlatformServices.IsSupported == OperatingSystem.IsWindows(), "The adapter is guarded by operating system.");
        if (!OperatingSystem.IsWindows())
            return;

        using var platform = new WindowsPlatformServices();
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Check(platform.DataDirectory == Path.Combine(localData, "Vstavka.Preview") &&
            platform.HistoryProtector is WindowsHistoryProtector &&
            WindowsPlatformServices.GetSingleInstanceName().StartsWith(@"Local\Vstavka.Preview.S-", StringComparison.Ordinal),
            "The Windows entry point uses the preview directory, instance name and DPAPI.");
        using var window = new WindowsMessageWindow();
        Check(window.Handle != 0, "A message-only window can be created without listening or registering a hotkey.");

        var images = new WindowsClipboardImages();
        byte[] expectedPixels = [0, 0, 255, 127, 255, 0, 0, 255];
        foreach (var topDown in new[] { true, false })
        {
            var dib = new byte[132];
            BinaryPrimitives.WriteInt32LittleEndian(dib, 124);
            BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), topDown ? -2 : 2);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x000000FF);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(52), 0xFF000000);
            expectedPixels.AsSpan(0, 4).CopyTo(dib.AsSpan(topDown ? 124 : 128));
            expectedPixels.AsSpan(4, 4).CopyTo(dib.AsSpan(topDown ? 128 : 124));
            var png = images.CapturePng(ClipboardFormat.DibV5, dib);
            var converted = png is null ? [] : images.CreateDibV5(png)[124..];
            Check(converted.SequenceEqual(expectedPixels),
                $"DIBV5 round-trip pixels (top down: {topDown}): {Convert.ToHexString(converted)}.");
        }

        var rgb = new byte[44];
        BinaryPrimitives.WriteInt32LittleEndian(rgb, 40);
        BinaryPrimitives.WriteInt32LittleEndian(rgb.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(rgb.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(rgb.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(rgb.AsSpan(14), 24);
        rgb[42] = 255;
        var rgbPng = images.CapturePng(ClipboardFormat.Dib, rgb);
        Check(rgbPng is not null && images.CreateDibV5(rgbPng)[124..].SequenceEqual(new byte[] { 0, 0, 255, 255 }),
            "A legacy RGB DIB captures as an opaque image.");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL {name}: {error}");
            Environment.ExitCode = 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
