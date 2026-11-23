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
| Global shortcut | Ctrl+Shift+V | ⇧⌘V | Ctrl+Alt+V via the extension |
| Paste into the previous app | `SendInput` | Cmd+V, needs Accessibility | virtual keyboard in the extension |
| Panel at the bottom | native | status window level on all Spaces | placed by the extension |

## Status

Covered by CI on all three systems: core and localization tests, adapter logic tests with
fakes, and headless tests of the panel, the first-run guide and Settings that render PNGs.
A separate CI job runs GNOME Shell 46 headless in Docker and checks the extension end to
end: clipboard events, writes, the shortcut, paste into a GTK4 window, panel placement and
the allowlist.

Tried on real hardware: macOS 27 on a MacBook (panel, Settings and permissions) and Ubuntu
with GNOME on Wayland (first-run guide and installing the GNOME helper). The GNOME helper
after signing out and back in still needs a check on a real session.

The new app has a four-step first-run window (welcome, shortcut with a recorder, the
permissions this platform needs, and a try-it step), a Settings window and start at login on
all three systems. Shortcuts can be any combination with Ctrl, Alt/Option or Meta; the
defaults are Ctrl+Shift+V on Windows, ⇧⌘V on macOS and Ctrl+Alt+V on Linux, where
Ctrl+Shift+V is paste in terminals. Inside the panel, macOS uses ⌘ where Windows and Linux
use Ctrl.

Not done yet: managing collections and sounds in the new app; automatic paste into Linux
terminals, which expect Ctrl+Shift+V; signed release packages.

## Trying the app

See [Building from source](../README.md#building-from-source) in the README. The new app
keeps its own history and never touches the Store app's data.
