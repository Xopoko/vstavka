using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Media;
using System.Windows.Threading;
using Vstavka.Services;

var results = new List<(string Name, Exception? Error)>();

Run("Global hotkey registration uses the chosen modifiers", () =>
{
    foreach (var shortcut in new[] { new Vstavka.Core.AppSettings().Hotkey, "Ctrl+Shift+V", " ctrl+shift+v ", "unknown", "", "Ctrl+Alt+V", "Alt+V", " alt+v " })
    {
        Native.HotkeyRegistrations.Clear();
        using var bridge = new SystemBridge(new Dispatcher());
        Check(bridge.SetHotkey(shortcut), "Shortcut registers successfully.");
        var registration = Native.HotkeyRegistrations.Single();
        var expected = shortcut.Trim().ToLowerInvariant() switch { "alt+v" => 1u, "ctrl+alt+v" => 3u, _ => 6u };
        Check(registration.Key == 0x56 && registration.Mods == (expected | 0x4000), "Ctrl+Shift+V by default, Ctrl+Alt+V or Alt+V when chosen, with no autorepeat.");
    }
});

Run("Busy replacement preserves the existing global shortcut", () =>
{
    using var bridge = new SystemBridge(new Dispatcher());
    Native.HotkeyRegistrations.Clear();
    Native.HotkeyUnregistrations.Clear();
    Check(bridge.SetHotkey("Ctrl+Alt+V"), "Initial shortcut registers.");
    var initialId = Native.HotkeyRegistrations.Single().Id;
    try
    {
        Native.HotkeyRegistrationSucceeds = false;
        Check(!bridge.SetHotkey("Alt+V") && Native.HotkeyUnregistrations.Count == 0, "Busy replacement does not unregister the working shortcut.");
        Native.HotkeyRegistrationSucceeds = true;
        Check(bridge.SetHotkey("Alt+V"), "Available replacement registers.");
        Check(Native.HotkeyUnregistrations.Single() == initialId && Native.HotkeyRegistrations[^1].Id != initialId, "Successful replacement releases only the previous registration.");
    }
    finally { Native.HotkeyRegistrationSucceeds = true; }
});

Run("Plain-text paste key down and up pass through without opening history", () =>
{
    using var fixture = new HookFixture(0x11, 0x10);
    fixture.Key(0x56);
    fixture.Key(0x56, up: true);
    fixture.Drain();
    Check(Native.ForwardedEvents == 2 && fixture.Pastes == 0, "Ctrl+Shift+V reaches the next hook without an extra paste notification.");
});

Run("Physical Ctrl+V and Shift+Insert dispatch exactly once per press", () =>
{
    foreach (var (key, modifier) in new[] { (0x56u, 0x11), (0x2Du, 0x10) })
    {
        using var fixture = new HookFixture(modifier);
        fixture.Key(key);
        Check(fixture.Pastes == 0, "Hook queues feedback instead of running it on the keyboard callback.");
        fixture.Drain();
        Check(fixture.Pastes == 1, "A physical paste shortcut produces feedback.");
        fixture.ResetDebounce();
        fixture.Key(key);
        fixture.Drain();
        Check(fixture.Pastes == 1, "Keyboard autorepeat while held produces no additional feedback.");
        fixture.Key(key, up: true);
        fixture.ResetDebounce();
        fixture.Key(key);
        fixture.Drain();
        Check(fixture.Pastes == 2, "Release permits a later physical paste.");
    }
});

Run("Injected replay shortcuts never emit a second paste notification", () =>
{
    foreach (var (key, modifier) in new[] { (0x56u, 0x11), (0x2Du, 0x10) })
    {
        using var fixture = new HookFixture(modifier);
        fixture.Key(key, injected: true);
        fixture.Key(key, up: true, injected: true);
        fixture.Drain();
        Check(fixture.Pastes == 0, "SendInput replay is ignored; its caller owns feedback.");
        fixture.ResetDebounce();
        fixture.Key(key);
        fixture.Drain();
        Check(fixture.Pastes == 1, "Ignoring replay does not suppress a subsequent physical shortcut.");
    }
});

Run("Drawer, modified, and unmodified shortcuts do not announce paste", () =>
{
    var cases = new (uint Key, int[] Modifiers)[]
    {
        (0x56, []), (0x2D, []), (0x56, [0x10]), (0x2D, [0x11]),
        (0x56, [0x11, 0x10]), (0x2D, [0x11, 0x10]),
        (0x56, [0x11, 0x12]), (0x2D, [0x10, 0x12]),
        (0x56, [0x11, 0x5B]), (0x2D, [0x10, 0x5B]),
        (0x56, [0x11, 0x5C]), (0x2D, [0x10, 0x5C]),
        (0x43, [0x11])
    };
    foreach (var (key, modifiers) in cases)
    {
        using var fixture = new HookFixture(modifiers);
        fixture.Key(key);
        fixture.Drain();
        Check(fixture.Pastes == 0, "Only the supported physical paste shortcuts announce paste.");
    }
});

Run("Debounce and hook forwarding preserve the keyboard event chain", () =>
{
    using var fixture = new HookFixture(0x11);
    fixture.SetLastPaste(Environment.TickCount64 + 10_000);
    fixture.Key(0x56);
    fixture.Drain();
    Check(fixture.Pastes == 0, "A shortcut inside the debounce interval is suppressed.");
    Check(fixture.Callback(-1, 0x100, 0) == 73, "A negative hook code is forwarded without reading event memory.");
    Check(Native.ForwardedEvents == 2, "Both handled and unhandled events reach the next hook.");
});

Run("Copy and paste waveforms are valid, short, distinct PCM clips", () =>
{
    var copy = SoundService.CreateWave(false, 0.45);
    var paste = SoundService.CreateWave(true, 0.45);
    foreach (var (wave, duration) in new[] { (copy, 0.105), (paste, 0.055) })
    {
        Check(Encoding.ASCII.GetString(wave, 0, 4) == "RIFF" && Encoding.ASCII.GetString(wave, 8, 8) == "WAVEfmt ", "RIFF/WAVE signature.");
        Check(BitConverter.ToInt32(wave, 4) == wave.Length - 8 && BitConverter.ToInt32(wave, 16) == 16, "Consistent RIFF and format lengths.");
        Check(BitConverter.ToInt16(wave, 20) == 1 && BitConverter.ToInt16(wave, 22) == 1, "Mono PCM.");
        Check(BitConverter.ToInt32(wave, 24) == 44_100 && BitConverter.ToInt32(wave, 28) == 88_200, "Correct sample and byte rates.");
        Check(BitConverter.ToInt16(wave, 32) == 2 && BitConverter.ToInt16(wave, 34) == 16, "16-bit samples and alignment.");
        Check(Encoding.ASCII.GetString(wave, 36, 4) == "data" && BitConverter.ToInt32(wave, 40) == wave.Length - 44, "Consistent data length.");
        var samples = Samples(wave);
        Check(samples.Length == (int)(44_100 * duration), "Requested clip duration.");
        Check(samples.Any(value => value != 0) && samples[0] == 0 && Math.Abs(samples[^1]) <= 1, "Nonempty sound starts and ends without a sample discontinuity.");
    }
    Check(!copy.SequenceEqual(paste), "Copy and paste have distinct audio.");
});

Run("Volume scales signal energy, and mute and range clamping are honored", () =>
{
    foreach (var paste in new[] { false, true })
    {
        var mute = SoundService.CreateWave(paste, 0);
        var low = SoundService.CreateWave(paste, 0.25);
        var high = SoundService.CreateWave(paste, 1);
        Check(Samples(mute).All(value => value == 0), "Zero volume creates a silent clip.");
        Check(mute.SequenceEqual(SoundService.CreateWave(paste, -1)), "Negative volume clamps to mute.");
        Check(high.SequenceEqual(SoundService.CreateWave(paste, 2)), "Excess volume clamps to full volume.");
        var energyRatio = Energy(high) / Energy(low);
        Check(energyRatio > 15.9 && energyRatio < 16.2, "Quarter amplitude has approximately one sixteenth the signal energy.");
        Check(Samples(high).All(value => Math.Abs((int)value) < short.MaxValue), "Full-volume PCM does not clip.");
    }
});

Run("Copy and paste use separate players; volume replacement disposes old resources", () =>
{
    SoundPlayer.Created.Clear();
    using var sounds = new SoundService();
    sounds.Copy();
    sounds.Paste();
    Check(SoundPlayer.Created.Count == 0, "Uninitialized sound requests are harmless.");
    sounds.SetVolume(0.45);
    var copy = SoundPlayer.Created[0];
    var paste = SoundPlayer.Created[1];
    sounds.Copy();
    sounds.Paste();
    Check(copy.Loaded && paste.Loaded && copy.Plays == 1 && paste.Plays == 1, "Each route plays its own loaded clip once.");
    sounds.SetVolume(0);
    Check(copy.Disposed && paste.Disposed && !copy.Stream.CanRead && !paste.Stream.CanRead, "Volume changes release old players and backing streams.");
    var mutedCopy = SoundPlayer.Created[2];
    var mutedPaste = SoundPlayer.Created[3];
    Check(Samples(((MemoryStream)mutedCopy.Stream).ToArray()).All(value => value == 0) && Samples(((MemoryStream)mutedPaste.Stream).ToArray()).All(value => value == 0), "Both replacement routes honor mute.");
    sounds.Copy();
    sounds.Paste();
    Check(mutedCopy.Plays == 1 && mutedPaste.Plays == 1 && copy.Plays == 1 && paste.Plays == 1, "Replaced players are never reused.");
    sounds.Dispose();
    sounds.Dispose();
    sounds.Copy();
    sounds.Paste();
    Check(mutedCopy.Disposed && mutedPaste.Disposed && !mutedCopy.Stream.CanRead && !mutedPaste.Stream.CanRead, "Repeated disposal releases both routes; later requests do not escape as exceptions.");
});

foreach (var result in results)
    Console.WriteLine(result.Error is null ? $"PASS {result.Name}" : $"FAIL {result.Name}: {result.Error.GetType().Name}: {result.Error.Message}");
Console.WriteLine($"{results.Count(x => x.Error is null)}/{results.Count} audio test groups passed. Recording boundaries only; no live clipboard, hooks, keys, windows, or audible playback.");
return results.Any(x => x.Error is not null) ? 1 : 0;

void Run(string name, Action test)
{
    try { test(); results.Add((name, null)); }
    catch (Exception exception) { results.Add((name, exception)); }
}

static short[] Samples(byte[] wave) => Enumerable.Range(0, (wave.Length - 44) / 2).Select(index => BitConverter.ToInt16(wave, 44 + index * 2)).ToArray();
static double Energy(byte[] wave) => Samples(wave).Sum(value => (double)value * value);
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class HookFixture : IDisposable
{
    private readonly SystemBridge bridge;
    private readonly Dispatcher dispatcher = new();
    internal Native.HookProc Callback { get; }
    internal int Pastes { get; private set; }
    internal HookFixture(params int[] modifiers)
    {
        Native.DownKeys.Clear();
        Native.DownKeys.UnionWith(modifiers);
        Native.ForwardedEvents = 0;
        bridge = new SystemBridge(dispatcher);
        bridge.PasteShortcutPressed += () => Pastes++;
        Callback = (Native.HookProc)typeof(SystemBridge).GetField("callback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
        ResetDebounce();
    }
    internal void Key(uint key, bool up = false, bool injected = false)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new Native.KBDLLHOOKSTRUCT { vk = key, flags = injected ? 0x10u : 0 }, pointer, false);
            if (Callback(0, up ? 0x101 : 0x100, pointer) != 73)
                throw new InvalidOperationException("Every keyboard event must preserve the hook-chain result.");
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    internal void Drain() => dispatcher.Drain();
    internal void ResetDebounce() => SetLastPaste(Environment.TickCount64 - 1000);
    internal void SetLastPaste(long value) => typeof(SystemBridge).GetField("lastPaste", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bridge, value);
    public void Dispose() => bridge.Dispose();
}
