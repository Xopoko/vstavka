# Cross-platform Vstavka (preview)

Vstavka is moving from the Windows-only WPF app to one Avalonia app for Windows, macOS and
Ubuntu. The Microsoft Store build keeps shipping from `src/Vstavka` (WPF) until the new app
reaches feature parity.

## How it is built

| Project | Role |
| --- | --- |
| `src/Vstavka.Core` | History, search, pins, collections, retention, the 24 language catalogs and encrypted storage: DPAPI on Windows, AES-256-GCM with a key in the Keychain or the Secret Service elsewhere |
| `src/Vstavka.Platform` | Contracts for clipboard, shortcut, paste, panel window and history protection, plus a demo implementation |
| `src/Vstavka.Platform.Windows` | Win32 clipboard listener, `RegisterHotKey`, `SendInput` paste |
| `src/Vstavka.Platform.MacOS` | `NSPasteboard` polling, Carbon shortcut, Cmd+V with Accessibility permission, Keychain key; see [Integration.md](../src/Vstavka.Platform.MacOS/Integration.md) |
| `src/Vstavka.Platform.Linux` | GNOME Shell extension client over D-Bus, X11 fallback, Secret Service key |
| `gnome-extension` | GNOME Shell helper for Wayland, where apps cannot watch the clipboard, place windows or paste; see its [README](../gnome-extension/README.md) |
| `src/Vstavka.Desktop` | The Avalonia app: panel, cards, search, keyboard, themes, tray icon |

Platform differences that shape the design:

| | Windows | macOS | Ubuntu (GNOME Wayland) |
| --- | --- | --- | --- |
| Clipboard changes | system notification | polling `changeCount`; access may need approval (macOS 15.4+) | only through the extension; X11 sessions use XFixes |
| Global shortcut | Ctrl+Shift+V | ⌘⇧V | Ctrl+Alt+V via the extension |
| Paste into the previous app | `SendInput` | Cmd+V, needs Accessibility | virtual keyboard in the extension |
| Panel at the bottom | native | status window level on all Spaces | placed by the extension |

## Status

Covered by CI on all three systems: core and localization tests, adapter logic tests with
fakes, and headless panel tests that render PNGs. A separate CI job runs GNOME Shell 46
headless in Docker and checks the extension end to end: clipboard events, writes, the
shortcut, paste into a GTK4 window, panel placement and the allowlist.

Checked on real hardware: a read-only probe on macOS 27 (pasteboard state, source icon,
shortcut registration) and the app bundle script.

The new app has a four-step first-run window (welcome, shortcut with a recorder, the
permissions this platform needs, and a try-it step), a Settings window and start at login on
all three systems. Shortcuts can be any combination with Ctrl, Alt/Option or Meta; the
defaults are Ctrl+Shift+V on Windows, ⌘⇧V on macOS and Ctrl+Alt+V on Linux, where
Ctrl+Shift+V is paste in terminals.

Not done yet: managing collections and sounds in the new app; trials in a real GNOME session;
signed release packages.

## Trying the preview

The preview keeps its own history and never touches the Store app's data.

**Windows.** Exit the installed Vstavka if it uses the same shortcut, then run
`dotnet run --project src/Vstavka.Desktop`. History lives in `%LOCALAPPDATA%\Vstavka.Preview`.
Add `--demo` for sample data without reading the clipboard.

**macOS.** Publish on any system, then build the app bundle on the Mac:

```sh
dotnet publish src/Vstavka.Desktop -c Release -r osx-arm64 --self-contained -o <publish-dir>
scripts/package-macos-app.sh <publish-dir> <output-dir>
```

Open `Vstavka.app` and press ⌘⇧V. The button in the panel footer asks macOS for clipboard
access and for Accessibility, which automatic paste needs.

**Ubuntu.** Install the extension as described in its [README](../gnome-extension/README.md),
add the published `Vstavka.Desktop` executable to `allowed-executables`, sign out and back in,
then start the app. On an X11 session the app works without the extension.
