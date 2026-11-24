# Platform integration

Vstavka uses Avalonia for Windows, macOS and Ubuntu. The Windows Store profile
reads history and settings from the earlier WPF app and retains its startup
task. See [MSIX preparation](../packaging/README.md).

## How it is built

| Project | Role |
| --- | --- |
| `src/Vstavka.Core` | History, search, pins, collections, retention, the 24 language catalogs and encrypted storage: DPAPI on Windows, AES-256-GCM with a key in the Keychain or the Secret Service elsewhere |
| `src/Vstavka.Platform` | Interfaces for clipboard, shortcut, paste and window adapters, plus demo services |
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
| Paste into the previous app | `SendInput` | Local build uses Cmd+V with Accessibility. App Store build copies for manual paste. | virtual keyboard in the extension |
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

For release checks on a real desktop, see the [manual checklist](acceptance.md).
