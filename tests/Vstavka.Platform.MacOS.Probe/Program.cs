using Vstavka.Platform.MacOS;

if (!MacPlatformServices.IsSupported)
{
    Console.Error.WriteLine("Run this probe on macOS.");
    return 1;
}

// Do not instantiate MacPlatformServices or start the monitor. This probe uses
// the metadata getters only and never asks for access or opens Keychain.
using var pool = new AutoreleasePool();
var board = new NativePasteboard();
var access = board.Access;
Console.WriteLine($"OS: macOS {Environment.OSVersion.Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
Console.WriteLine($"Pasteboard changeCount: {board.ChangeCount}");
Console.WriteLine($"Pasteboard accessBehavior: {access} ({(long)access})");
var types = board.ReadTypeNames();
Console.WriteLine($"Pasteboard types: {(types.Count == 0 ? "(none)" : string.Join(", ", types.Select(Escape)))}");
if (board.Access != access)
    Console.WriteLine($"Pasteboard accessBehavior after type metadata lookup: {board.Access}");

Console.WriteLine($"AXIsProcessTrusted: {Accessibility.IsTrusted()}");
var app = RunningApps.Describe(RunningApps.Frontmost);
Console.WriteLine($"Frontmost app: {Escape(app?.Name ?? "(unavailable)")} ({Escape(app?.Id ?? "no bundle ID")})");
var icon = app?.IconPng;
var iconValid = ClipboardCapturePolicy.IsSourceIconPng(icon) &&
    ClipboardCapturePolicy.TryGetPngSize(icon, out var width, out var height) && width == 32 && height == 32;
Console.WriteLine($"Frontmost icon valid 32x32 PNG: {iconValid} ({icon?.Length ?? 0} bytes)");

HotkeyGesture.TryParse(HotkeyGesture.Default, out var gesture);
var registration = CarbonHotkey.TryRegister(gesture, static () => { }, out var operation, out var status);
Console.WriteLine($"Hotkey {HotkeyGesture.Default}: {operation} OSStatus {status}");
if (registration is not null)
{
    registration.Dispose();
    Console.WriteLine($"Hotkey unregister: OSStatus {registration.UnregisterStatus}");
}
return 0;

static string Escape(string value) => string.Concat(value.Select(c => char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString()));
