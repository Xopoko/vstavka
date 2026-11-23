# Vstavka on GNOME

The extension lets Vstavka read clipboard changes, set the clipboard, register a shortcut, restore focus and paste, and place its panel on GNOME Wayland.

## Installation

From the repository root in a GNOME session:

~~~sh
uuid=vstavka@xopoko.github.io
dest="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$uuid"
install -d "$dest"
cp -R "gnome-extension/$uuid/." "$dest/"
glib-compile-schemas --strict "$dest/schemas"
gnome-extensions enable "$uuid"
~~~

If GNOME does not see the newly installed extension, log out and back in, then run the enable command. When updating an installed copy, recompile the schema and log out and back in to load the new code. Git ignores the generated `gschemas.compiled`.

Authorize the installed app before launching it. Use the absolute path printed by `readlink -f /path/to/Vstavka.Desktop` and replace the example path in this command:

~~~sh
gsettings --schemadir "$dest/schemas" set io.github.xopoko.Vstavka.Shell allowed-executables \
    "['/absolute/path/to/Vstavka.Desktop']"
~~~

Launch the dedicated Vstavka executable. Adding a shared interpreter such as `dotnet` would give every program started through it the extension's permissions. Restart Vstavka after changing the extension or allowlist.

The panel must use title `Vstavka` and WM_CLASS or Wayland app ID `io.github.xopoko.Vstavka`. The extension places it at the bottom center of the active monitor's work area. The app calls `CaptureTarget` and `PreparePanel` before showing it, and calls `Paste` after hiding it.

## D-Bus

The service owns `io.github.xopoko.Vstavka.Shell1` on the session bus while enabled. The object path is `/io/github/xopoko/Vstavka/Shell`.

~~~xml
<node>
  <interface name="io.github.xopoko.Vstavka.Shell1">
    <method name="SubscribeClipboard"/>
    <method name="UnsubscribeClipboard"/>
    <method name="SetClipboard">
      <arg name="mime" type="s" direction="in"/>
      <arg name="data" type="ay" direction="in"/>
      <arg name="success" type="b" direction="out"/>
    </method>
    <method name="SetAccelerator">
      <arg name="accelerator" type="s" direction="in"/>
      <arg name="success" type="b" direction="out"/>
    </method>
    <method name="ReleaseHotkey"/>
    <method name="CaptureTarget"/>
    <method name="Paste">
      <arg name="success" type="b" direction="out"/>
    </method>
    <method name="PreparePanel"/>
    <signal name="ClipboardChanged">
      <arg name="mime" type="s"/>
      <arg name="data" type="ay"/>
      <arg name="desktop_id" type="s"/>
      <arg name="display_name" type="s"/>
      <arg name="icon_path" type="s"/>
    </signal>
    <signal name="HotkeyPressed"/>
  </interface>
</node>
~~~

- A client uses one connection for subscription and signal handling. The first allowed clipboard subscriber reserves the slot until it disconnects or its authorization is revoked. `UnsubscribeClipboard` turns off notifications for that connection. A second allowed connection gets `io.github.xopoko.Vstavka.Shell1.ClipboardBusy`. Disconnecting also releases the shortcut.
- Clipboard notifications report new `CLIPBOARD` owners. In priority order the extension reads `text/uri-list`, `image/png`, UTF-8 text, and legacy X11 `STRING`. The cap is 10 MiB for PNG and 4 MiB for text and URI lists. The Linux adapter converts local `file:` URIs to paths and applies the Core limits for text, paths and images.
- Attribution uses the focused app at the time of the owner change. Unavailable metadata is an empty string. The theme lookup prefers a PNG at 32 pixels. The adapter accepts PNG paths up to 32 × 32 and 64 KiB.
- `SetClipboard` accepts `text/plain;charset=utf-8`, `text/uri-list`, and `image/png`. The extension suppresses its own owner change. The app's clipboard writer sends file paths as newline-delimited plain text.
- `SetAccelerator` accepts `<Control><Shift>v`, `<Control><Alt>v`, and `<Alt>v`. The schema defaults to Ctrl+Shift+V. A successful call registers the keybinding for that caller and saves its accelerator. An unavailable accelerator returns false and preserves the previous binding.
- `Paste` returns true after the extension has focused the target and queued Ctrl+V. The receiving application decides how to handle the shortcut.

### Access

The allowlist starts empty. For each new caller, GNOME asks the session bus for the process ID behind its unique connection name, then reads that process's executable path from `/proc/<pid>/exe`. The path must exactly match an absolute entry in `allowed-executables`. Unknown callers receive `io.github.xopoko.Vstavka.Shell1.AccessDenied` for every method in an unlocked session. Clipboard and shortcut signals go directly to the authorized connection. The extension forgets the connection when it closes. Removing an executable from the allowlist revokes its clipboard slot and shortcut.

An allowed executable can read and replace the clipboard and trigger paste in the unlocked GNOME session. Only add trusted executables that other users cannot replace. The check keeps ordinary programs out, but it is not a sandbox: code that already runs as your user can still start an allowed executable with injected code.

### Private selections

An offered `x-kde-passwordManagerHint` with value `secret` is skipped. Missing, unreadable, empty or oversized values for an advertised hint also skip the copy. The extension skips Mutter's in-memory clipboard owners because clipboard restoration can lose the source's privacy marker. The extension and X11 adapter recognize the following presence-only markers, using case-insensitive names:

- `application/x-keepassxc`, `application/x-keepassxc-secret`, `application/x-gnome-password`, `application/x-vstavka-private`
- `org.nspasteboard.ConcealedType`, `org.nspasteboard.TransientType`, `org.nspasteboard.AutoGeneratedType`
- `Clipboard Viewer Ignore`, `ExcludeClipboardContentFromMonitorProcessing`

### Linux adapter

An X11 session without the extension uses a separate hidden X11 window, XFixes owner notifications, selection conversions, keyboard grabs and XTest. The X11 backend reports the focused window's class when its window manager exposes it. Without the extension on Wayland, Vstavka exposes the install notice. Encrypted history uses an unlocked default Secret Service collection and an entry in the `io.github.xopoko.Vstavka.HistoryKey` schema. Data lives under `$XDG_DATA_HOME/vstavka`, or `~/.local/share/vstavka`.

## Checks

~~~sh
dotnet run --project tests/Vstavka.Platform.Linux.Tests -c Debug
bash gnome-extension/tests/run-headless.sh
~~~

The .NET tests use fakes and run on Windows, Linux, and macOS. The second command works on a Linux Docker host and runs in the independent GNOME job in CI. It starts GNOME Shell 46 on Ubuntu 24.04 with a software renderer, a virtual monitor and a private D-Bus session. The checks exercise the allowed native Wayland GTK client, refusal and signal isolation for a second executable path, and subscription ownership. The job prints the Shell log and fails on JavaScript, extension or critical errors. Set `VSTAVKA_GNOME_LOG_DIR` to also write `gnome-shell.log` to a host directory. CI uploads that file on failure. The minimal session can emit messages about absent desktop services such as GDM and the calendar service.
