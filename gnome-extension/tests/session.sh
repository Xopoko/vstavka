#!/usr/bin/env bash
set -euo pipefail
export HOME=$(mktemp -d)
export XDG_DATA_HOME=$HOME/.local/share
export XDG_CONFIG_HOME=$HOME/.config
export XDG_RUNTIME_DIR=$HOME/runtime
export XDG_SESSION_TYPE=wayland
export XDG_CURRENT_DESKTOP=GNOME
export GDK_BACKEND=wayland
export WAYLAND_DISPLAY=vstavka-test
export LIBGL_ALWAYS_SOFTWARE=1
export NO_AT_BRIDGE=1
export GTK_A11Y=none
export GSETTINGS_BACKEND=keyfile
export VSTAVKA_ALLOWED_CLIENT=$HOME/allowed-client
export VSTAVKA_UNLISTED_CLIENT=$HOME/unlisted-client

uuid=vstavka@xopoko.github.io
snap_uuid=vstavka-snap@xopoko.github.io
extensions=$XDG_DATA_HOME/gnome-shell/extensions
mkdir -m 700 "$XDG_RUNTIME_DIR"
mkdir -p "$extensions" "$XDG_DATA_HOME/applications" /run/dbus
cp -r "/vstavka/$uuid" /vstavka/tests/probe@vstavka.test "$extensions"
mkdir "$extensions/$snap_uuid"
cp "/vstavka/$uuid/"{extension-core.js,snap-peer.js,clipboard.js,windows.js,shell.xml} "$extensions/$snap_uuid/"
cp /vstavka/snap-extension.js "$extensions/$snap_uuid/extension.js"
cp /vstavka/snap-metadata.json "$extensions/$snap_uuid/metadata.json"
cp /vstavka/tests/io.github.xopoko.VstavkaTest{,Terminal}.desktop "$XDG_DATA_HOME/applications/"
icons=$XDG_DATA_HOME/icons/hicolor/32x32/apps
mkdir -p "$icons"
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMQMgn7DwAClAGcHVtGXwAAAABJRU5ErkJggg==' |
    base64 --decode > "$icons/vstavka-test-editor.png"
glib-compile-schemas --strict "$extensions/$uuid/schemas"
cp "$(command -v gjs)" "$VSTAVKA_ALLOWED_CLIENT"
cp "$(command -v gjs)" "$VSTAVKA_UNLISTED_CLIENT"
# Noble creates this empty directory even in a container without systemd-logind.
if [[ -d /run/systemd/seats ]]; then
    rmdir /run/systemd/seats
fi
ulimit -c 0
dbus-daemon --system --fork
gsettings set org.gnome.shell disable-user-extensions false
gsettings set org.gnome.shell enabled-extensions "['$uuid', 'probe@vstavka.test']"
log=$HOME/gnome-shell.log
gnome-shell --version
gnome-shell --headless --wayland --no-x11 --virtual-monitor 1024x768 \
    --wayland-display "$WAYLAND_DISPLAY" --sm-disable >"$log" 2>&1 &
shell_pid=$!

cleanup() {
    local result=$?
    kill "$shell_pid" 2>/dev/null || true
    wait "$shell_pid" 2>/dev/null || true
    if [[ -d /vstavka-test-logs ]]; then
        cat "$log" > /vstavka-test-logs/gnome-shell.log
    fi
    echo '--- GNOME Shell log ---'
    cat "$log"
    exit "$result"
}
trap cleanup EXIT
timeout 30 gdbus wait --session io.github.xopoko.Vstavka.Shell1
timeout 30 gdbus wait --session io.github.xopoko.Vstavka.TestProbe
test -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"
echo 'PASS Wayland compositor, extension and D-Bus service started'
probe=io.github.xopoko.Vstavka.TestProbe
probe_path=/io/github/xopoko/Vstavka/TestProbe
ready=
for attempt in {1..100}; do
    ready=$(gdbus call --session --dest "$probe" --object-path "$probe_path" --method "$probe.Ready")
    if [[ "$ready" == '(true,)' ]]; then
        break
    fi
    sleep 0.1
done
test "$ready" = '(true,)'
gdbus call --session --dest "$probe" --object-path "$probe_path" --method "$probe.InitInput" >/dev/null
timeout 5 "$VSTAVKA_ALLOWED_CLIENT" -m /vstavka/tests/peer.js blank
gsettings --schemadir "$extensions/$uuid/schemas" set io.github.xopoko.Vstavka.Shell \
    allowed-executables "['$VSTAVKA_ALLOWED_CLIENT']"
timeout 40 "$VSTAVKA_ALLOWED_CLIENT" -m /vstavka/tests/client.js
timeout 5 "$VSTAVKA_ALLOWED_CLIENT" -m /vstavka/tests/peer.js acquire
timeout 5 gnome-extensions disable "$uuid"
for attempt in {1..50}; do
    owner=$(gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
        --method org.freedesktop.DBus.NameHasOwner io.github.xopoko.Vstavka.Shell1)
    if [[ "$owner" == '(false,)' ]]; then
        break
    fi
    sleep 0.1
done
test "$owner" = '(false,)'
echo 'PASS disabling the extension removes its D-Bus name'

snap_socket=$XDG_RUNTIME_DIR/snap.vstavka/vstavka-shell.sock
test ! -e "$snap_socket"
gsettings set org.gnome.shell enabled-extensions "['probe@vstavka.test', '$snap_uuid']"
for attempt in {1..50}; do
    if [[ -S "$snap_socket" ]]; then
        break
    fi
    sleep 0.1
done
test -S "$snap_socket" || { echo 'Snap extension did not start its private socket' >&2; exit 1; }
owner=$(gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
    --method org.freedesktop.DBus.NameHasOwner io.github.xopoko.Vstavka.Shell1)
test "$owner" = '(false,)'
timeout 10 "$VSTAVKA_UNLISTED_CLIENT" -m /vstavka/tests/peer.js snap-denied "$snap_socket"
gsettings set org.gnome.shell enabled-extensions "['probe@vstavka.test']"
for attempt in {1..50}; do
    if [[ ! -e "$snap_socket" ]]; then
        break
    fi
    sleep 0.1
done
test ! -e "$snap_socket" || { echo 'Snap extension left its private socket after disable' >&2; exit 1; }
echo 'PASS Snap-only extension claims no bus name and removes its private socket on disable'
if grep -Ei '(JS ERROR|Vstavka clipboard|Extension .* error|CRITICAL)' "$log"; then
    echo 'Unexpected Shell, JavaScript or extension errors in GNOME Shell log' >&2
    exit 1
fi
echo 'PASS no JavaScript, extension or critical errors in GNOME Shell log'
