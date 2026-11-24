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
        Run("Paste sound shortcuts ignore other chords and held keys", PasteSoundShortcuts);
        if (OperatingSystem.IsWindows())
            Run("Copy and paste sounds contain distinct, volume-scaled PCM", Waveforms);
        Run("Preview startup registration and failure", StartupRegistration);
        Run("Store profile preserves legacy history and separates first-run consent", StoreUpgrade);
        Run("Paste restores the target and handles failure", Paste);
        Run("Preview isolation and Windows image conversion", IsolationAndCodecs);
    }

    private static void PasteSoundShortcuts()
    {
        var shortcut = new PasteShortcutState();
        Check(!shortcut.Press(PasteShortcutState.V, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 1000),
            "The default panel shortcut must not play the paste sound.");
        Check(shortcut.Press(PasteShortcutState.V, HotkeyModifiers.Ctrl, 1100) &&
              !shortcut.Press(PasteShortcutState.V, HotkeyModifiers.Ctrl, 1200),
            "Ctrl+V plays once while V stays down, including across repeated key events.");
        shortcut.Release(PasteShortcutState.V);
        Check(shortcut.Press(PasteShortcutState.Insert, HotkeyModifiers.Shift, 1250),
            "Shift+Insert plays after the earlier key is released and the debounce window ends.");
        shortcut.Release(PasteShortcutState.Insert);
        Check(!shortcut.Press(PasteShortcutState.V, HotkeyModifiers.Ctrl, 1290),
            "A second shortcut within the debounce window is silent.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Waveforms()
    {
        var copy = SoundService.CreateWave(false, 1);
        var paste = SoundService.CreateWave(true, 1);
        var quiet = SoundService.CreateWave(false, 0.25);
        var mute = SoundService.CreateWave(true, 0);
        foreach (var wave in new[] { copy, paste })
            Check(wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) && wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) &&
                BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24)) == 44100 &&
                BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40)) == wave.Length - 44,
                "The WAV header describes its sample rate and payload.");

        Check(copy.Length > paste.Length && paste.Length < 6000 && mute.AsSpan(44).IndexOfAnyExcept((byte)0) < 0,
            "Paste is a shorter sound and mute has no signal.");
        var copyPeak = Peak(copy);
        Check(copyPeak is > 0 and < short.MaxValue && Math.Abs(Peak(quiet) * 4 - copyPeak) < 4,
            "Reducing the volume reduces the PCM amplitude without clipping.");

        static int Peak(byte[] wave)
        {
            var peak = 0;
            for (var offset = 44; offset < wave.Length; offset += sizeof(short))
                peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(offset))));
            return peak;
        }
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
        var notices = new List<string>();
        monitor.Notice += notice => notices.Add(notice.Code);
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
        Check(captures.Count == 0 && native.PayloadReads == 0 && icons.Paths.Count == 0 && notices.Count == 0,
            "Privacy formats silently skip data and icon retrieval, including a malformed opt-in flag.");

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

        native.NewText(new string('a', 2 * 1024 * 1024));
        native.Data[ClipboardFormat.Png] = [21];
        native.Data[ClipboardFormat.Files] = [1];
        native.Files = [@"C:\Synthetic\test.txt"];
        messages.Update();
        ui.Drain();
        Check(captures[^1].Item is { Kind: ClipKind.Files } && captures[^1].Item.Files.SequenceEqual(native.Files) &&
            images.Captures == 0 && notices.Count == 0, "A file drop takes precedence even when its alternate text is oversized.");
        native.NewText("fallback text");
        native.Data[ClipboardFormat.DibV5] = [37];
        messages.Update();
        ui.Drain();
        Check(captures[^1].Item is { Kind: ClipKind.Image } && captures[^1].Item.ImageBytes!.SequenceEqual(new byte[] { 37 }) &&
            images.Captures == 1, "DIB images are converted to PNG before capture.");

        native.NewText(new string('a', HistoryStore.MaxTextLength + 1));
        messages.Update();
        ui.Drain();
        native.NewText("fallback text");
        native.Data[ClipboardFormat.Files] = [1];
        native.Files = null;
        messages.Update();
        ui.Drain();
        Check(captures.Count == 3 && notices.SequenceEqual(["clipboard-item-too-large", "clipboard-read-failed"]),
            "Oversized text and unreadable file drops report why they could not be captured.");
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

        var missing = new ClipItem { Kind = ClipKind.Files, Files = [Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))] };
        var attemptsBeforeMissing = native.WriteAttempts;
        try { clipboard.Write(missing, false); throw new Exception("A missing file was replayed."); }
        catch (FileNotFoundException error)
        {
            Check(error.FileName == missing.Files[0] && native.WriteAttempts == attemptsBeforeMissing,
                "A missing file is rejected before the clipboard is changed.");
        }
        clipboard.Write(missing, true);
        Check(Encoding.Unicode.GetString(native.Data[ClipboardFormat.Text]) == missing.Files[0] + '\0',
            "The missing file's path can still be copied as text.");
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
        foreach (var unusable in new[] { "V", "Shift+V", "Ctrl+F25", "Ctrl+Enter" })
            Check(!hotkey.TryRegister(unusable), $"{unusable} is unusable.");
        Check(messages.Attempts.Single() == (1, 0x4006u, 0x56u),
            "Unusable gestures do not reach the native registration boundary.");

        messages.RejectHotkey = true;
        replacement.Run(() => Check(!hotkey.TryRegister("Ctrl+Shift+Z"), "A taken shortcut is rejected."));
        Check(messages.Hotkeys.Keys.SequenceEqual([1]), "The original registration remains after failure.");
        messages.Press(1);
        messages.Press(2);
        replacement.Drain();
        Check(delivered.Count == 0, "A failed replacement does not change the event context.");
        original.Drain();
        Check(delivered.Count == 1 && delivered[0] == original, "The original shortcut still fires on its context.");

        messages.RejectHotkey = false;
        messages.RejectUnregisterId = 1;
        replacement.Run(() => Check(!hotkey.TryRegister("Ctrl+Shift+Z"), "Replacement rolls back if the old shortcut cannot be retired."));
        Check(messages.Hotkeys.Keys.SequenceEqual([1]), "Rollback keeps only the original shortcut.");
        messages.RejectUnregisterId = null;
        replacement.Run(() => Check(hotkey.TryRegister("Ctrl+Shift+Z"), "The next shortcut registers."));
        messages.Press(2);
        replacement.Run(() => Check(hotkey.TryRegister("Ctrl+Shift+A") && hotkey.TryRegister(" shift+control+a "),
            "A different key with the same modifiers registers, and aliases are idempotent."));
        messages.Press(1, key: 0x56);
        messages.Press(1);
        replacement.Drain();
        Check(delivered.Count == 2 && delivered[^1] == replacement,
            "Both stale Windows messages and stale queued callbacks are discarded.");
        Check(messages.Attempts.SequenceEqual(new (int, uint, uint)[]
        {
            (1, 0x4006, 0x56), (2, 0x4006, 0x5A), (2, 0x4006, 0x5A), (2, 0x4006, 0x5A), (1, 0x4006, 0x41)
        }), "Letter shortcuts map to the expected virtual keys and include MOD_NOREPEAT.");

        foreach (var (gesture, modifiers, key) in new (string, uint, uint)[]
        {
            ("Win+Space", 0x4008, 0x20), ("Ctrl+Alt+Shift+Meta+F1", 0x400F, 0x70),
            ("Alt+F24", 0x4001, 0x87), ("Ctrl+0", 0x4002, 0x30), ("Ctrl+9", 0x4002, 0x39)
        })
        {
            Check(hotkey.TryRegister(gesture), $"{gesture} registers.");
            var attempt = messages.Attempts[^1];
            Check(attempt.Modifiers == modifiers && attempt.Key == key,
                $"{gesture} registers with the expected flags and virtual key.");
        }

        hotkey.Dispose();
        Check(messages.Hotkeys.Count == 0, "Disposal unregisters the active shortcut.");
    }

    private static void StartupRegistration()
    {
        const string path = @"C:\Program Files\Vstavka Preview\Vstavka.exe";
        const string preview = "Vstavka.Preview";
        const string legacy = @"""C:\Vstavka\Vstavka.exe"" --background";
        var runKey = new RunKeyDouble();
        runKey.Values["Vstavka"] = legacy;
        var startup = new WindowsStartupRegistration(runKey, path);
        if (!OperatingSystem.IsWindows())
        {
            Check(!startup.IsSupported && !startup.IsEnabled && !startup.TrySetEnabled(true) &&
                !startup.TrySetEnabled(false) && runKey.Values.Count == 1,
                "Startup registration is guarded off Windows.");
            return;
        }

        Check(startup.IsSupported && !startup.IsEnabled, "The WPF entry alone does not enable preview startup.");
        runKey.Values[preview] = $"{legacy} \"{path}\"";
        Check(!startup.IsEnabled, "A different executable does not count even if the current path is an argument.");
        Check(startup.TrySetEnabled(true) && startup.IsEnabled && runKey.Values[preview] == $"\"{path}\" --background",
            "Enabling writes the quoted executable and background flag under the preview value name.");
        runKey.Values[preview] = $"\"{path.ToLowerInvariant()}\"";
        Check(startup.IsEnabled, "Enabled state matches the executable path case-insensitively, independent of arguments.");

        runKey.RefuseAccess = true;
        Check(!startup.IsEnabled && !startup.TrySetEnabled(true) && !startup.TrySetEnabled(false) &&
            runKey.Values.ContainsKey(preview), "Registry access failures return false and leave the entry in place.");
        runKey.RefuseAccess = false;
        Check(startup.TrySetEnabled(false) && startup.TrySetEnabled(false) && !startup.IsEnabled &&
            runKey.Values.Count == 1 && runKey.Values["Vstavka"] == legacy,
            "Disabling removes only the preview entry and succeeds when repeated.");
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
        Check(paste.PasteAsync().GetAwaiter().GetResult() == PasteResult.Manual && native.Restored.Count == 0,
            "Opening from Vstavka clears the previous external paste target.");

        native.ForegroundWindow = 1;
        paste.CaptureTarget();
        native.ForegroundWindow = 2;
        native.ModifiersDown = true;
        var delays = 0;
        onDelay = () => { if (++delays == 2) native.ModifiersDown = false; };
        Check(paste.PasteAsync().GetAwaiter().GetResult() == PasteResult.ShortcutSent && native.Restored.SequenceEqual(new nint[] { 1 }) &&
            native.SendAttempts == 1 && delays == 2, "Paste restores the recorded external target and waits for released keys.");

        onDelay = null;
        native.CanRestore = false;
        Check(paste.PasteAsync().GetAwaiter().GetResult() == PasteResult.Manual && native.SendAttempts == 1,
            "Failed foreground restoration does not send keys and leaves the item for manual paste.");
        native.CanRestore = true;
        native.CanSend = false;
        Check(paste.PasteAsync().GetAwaiter().GetResult() == PasteResult.Manual && native.SendAttempts == 2,
            "Blocked input leaves the item for manual paste.");

        onDelay = () => sequence++;
        var restoreAttempts = native.Restored.Count;
        Check(paste.PasteAsync().GetAwaiter().GetResult() == PasteResult.ClipboardChanged && native.Restored.Count == restoreAttempts &&
            native.SendAttempts == 2, "Changing the clipboard before paste cancels the request without offering manual paste.");
        sequence = 7;
        native.Processes[1] = 13;
        Check(paste.PasteAsync().GetAwaiter().GetResult() == PasteResult.Manual && native.SendAttempts == 2,
            "A recycled window handle cannot receive the paste.");
    }

    private static void StoreUpgrade()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "VstavkaStoreProfileTests-" + Guid.NewGuid().ToString("N"));
        var profile = new WindowsStoreProfile(localAppData);
        try
        {
            Check(profile.DataDirectory == Path.Combine(localAppData, "Vstavka") && !profile.HasClipboardConsent,
                "The Store uses the original history directory and starts without consent on a new install.");
            var settings = new AppSettings { Hotkey = "Alt+V", StartAtLogin = false };
            Check(profile.TryGrantClipboardConsent() && new WindowsStoreProfile(localAppData).HasClipboardConsent,
                "Explicit consent survives restart.");
            profile.ApplyLegacyPreferences(settings);
            Check(!settings.OnboardingCompleted && settings.PanelShortcut is null,
                "New users continue the new guide on the next launch.");

            File.WriteAllText(Path.Combine(profile.DataDirectory, WindowsStoreProfile.LegacyConsentFile), "WPF consent");
            if (OperatingSystem.IsWindows())
            {
                var wpfHistory = new HistoryStore(profile.DataDirectory, new WindowsHistoryProtector()) { Settings = settings };
                wpfHistory.Add(new ClipItem { Text = "existing clipboard item", IsPinned = true });
                wpfHistory.Save();
                var storeHistory = new HistoryStore(profile.DataDirectory, new WindowsHistoryProtector());
                storeHistory.Load();
                Check(storeHistory.Items.Single() is { Text: "existing clipboard item", IsPinned: true },
                    "The Store opens the original encrypted WPF history.");
                settings = storeHistory.Settings;
            }
            profile.ApplyLegacyPreferences(settings);
            Check(settings.OnboardingCompleted && settings.PanelShortcut == "Alt+V" && !settings.StartAtLogin,
                "Existing consent skips first run and carries over the old shortcut and startup preference.");
            settings.PanelShortcut = "Ctrl+Shift+X";
            profile.ApplyLegacyPreferences(settings);
            Check(settings.PanelShortcut == "Ctrl+Shift+X", "Future launches preserve the new user-defined shortcut.");
        }
        finally
        {
            if (Directory.Exists(profile.DataDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(profile.DataDirectory))
                    File.Delete(file);
                Directory.Delete(profile.DataDirectory);
                Directory.Delete(localAppData);
            }
        }
    }

    private static void IsolationAndCodecs()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var platform = new WindowsPlatformServices();
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Check(platform.DataDirectory == Path.Combine(localData, "Vstavka.Preview") &&
            platform.HistoryProtector is WindowsHistoryProtector,
            "The Windows entry point uses the preview directory and DPAPI.");
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
