#!/usr/bin/env bash
set -euo pipefail
extension_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
image=${VSTAVKA_TEST_IMAGE:-ubuntu:24.04}
# An Ubuntu mirror to use inside the image, for example the CI provider's own when archive.ubuntu.com is slow.
mirror=${VSTAVKA_APT_MIRROR:-}
log_mount=()
if [[ -n "${VSTAVKA_GNOME_LOG_DIR:-}" ]]; then
    mkdir -p -- "$VSTAVKA_GNOME_LOG_DIR"
    log_dir=$(cd -- "$VSTAVKA_GNOME_LOG_DIR" && pwd)
    log_mount=(--mount "type=bind,src=$log_dir,dst=/vstavka-test-logs")
fi
docker run --rm --label io.github.xopoko.vstavka.test=headless --cpus 2 --memory 2g \
    --env "VSTAVKA_APT_MIRROR=$mirror" \
    --mount "type=bind,src=$extension_dir,dst=/vstavka,readonly" "${log_mount[@]}" "$image" bash -ec '
        if ! command -v gnome-shell >/dev/null || ! command -v gjs >/dev/null; then
            export DEBIAN_FRONTEND=noninteractive
            sources=/etc/apt/sources.list.d/ubuntu.sources
            if [[ -n "$VSTAVKA_APT_MIRROR" && -f "$sources" ]]; then
                sed -i -E "s#http://(archive|security)[.]ubuntu[.]com/ubuntu#$VSTAVKA_APT_MIRROR#g" "$sources"
            fi
            # A stalled mirror fails here with the install log instead of running into the job timeout.
            apt=(apt-get -o Acquire::Retries=3 -o Acquire::http::Timeout=30)
            echo "Installing GNOME Shell from ${VSTAVKA_APT_MIRROR:-the default Ubuntu mirror}"
            "${apt[@]}" update -qq
            "${apt[@]}" install -y -qq --no-install-recommends gnome-shell gjs gir1.2-gtk-4.0 \
                dbus dbus-x11 dconf-gsettings-backend libgl1-mesa-dri >/tmp/install.log 2>&1 ||
                { tail -80 /tmp/install.log; exit 1; }
        fi
        exec dbus-run-session -- bash /vstavka/tests/session.sh
    '
