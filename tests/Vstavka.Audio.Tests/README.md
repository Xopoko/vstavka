# Audio boundary checks

Run `dotnet run --project tests/Vstavka.Audio.Tests -c Release` from the repository root.

This dependency-free console harness compiles the production `SystemBridge.cs`
and `SoundService.cs` unchanged, with recording native, dispatcher, window, and
audio boundaries. It exercises global hotkey registration and replacement failures,
Ctrl+Shift+V pass-through, physical shortcut filtering, injected replay
suppression, repeat/debounce handling, PCM encoding and volume, and audio
resource replacement/disposal. It does not register a real hook, access the
clipboard, inspect or inject keyboard input, create a real window, play sound,
or change device volume.

It proves the linked service behavior against these explicit boundaries. It
does not prove that Windows delivers hook notifications on a particular
machine, that another application accepts paste, or that audio is audible.
Application-level setting gates and source-replay suppression are reviewed in
`App.xaml.cs` and `ClipboardService.cs`; they are not exercised by this harness.
