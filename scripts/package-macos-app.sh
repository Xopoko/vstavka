#!/bin/bash
# Assembles Vstavka.app on macOS from a self-contained osx-arm64 or osx-x64 publish of
# src/Vstavka.Desktop. The publish can come from any OS; this step needs only macOS tools.
#
#   dotnet publish src/Vstavka.Desktop -c Release -r osx-arm64 --self-contained -o <publish-dir>
#   scripts/package-macos-app.sh <publish-dir> <output-dir> [version]
#
# The bundle is signed ad hoc for local testing. Releases need a Developer ID signature and
# notarization instead.
set -euo pipefail

if [[ "$(uname)" != "Darwin" ]]; then
    echo "Run this script on macOS." >&2
    exit 1
fi
if [[ $# -lt 2 ]]; then
    echo "Usage: $0 <publish-dir> <output-dir> [version]" >&2
    exit 1
fi

publish_dir="$(cd "$1" && pwd)"
output_dir="$2"
version="${3:-0.3.0}"
script_dir="$(cd "$(dirname "$0")" && pwd)"
icon_source="$script_dir/../src/Vstavka/Assets/Vstavka.png"
executable="Vstavka.Desktop"

if [[ ! -f "$publish_dir/$executable" ]]; then
    echo "$publish_dir does not contain $executable." >&2
    exit 1
fi

mkdir -p "$output_dir"
bundle="$(cd "$output_dir" && pwd)/Vstavka.app"
if [[ -e "$bundle" ]]; then
    echo "$bundle already exists; remove it first." >&2
    exit 1
fi

mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
cp -R "$publish_dir/." "$bundle/Contents/MacOS/"
chmod +x "$bundle/Contents/MacOS/$executable"

icon_key=""
if [[ -f "$icon_source" ]] && command -v iconutil >/dev/null && command -v sips >/dev/null; then
    iconset="$(mktemp -d)/Vstavka.iconset"
    mkdir -p "$iconset"
    for size in 16 32 64 128 256; do
        sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    done
    for size in 16 32 64 128; do
        cp "$iconset/icon_$((size * 2))x$((size * 2)).png" "$iconset/icon_${size}x${size}@2x.png"
    done
    rm "$iconset/icon_64x64.png" "$iconset/icon_64x64@2x.png"
    iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/Vstavka.icns"
    rm -rf "$(dirname "$iconset")"
    icon_key="    <key>CFBundleIconFile</key><string>Vstavka</string>"
fi

cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key><string>io.github.xopoko.Vstavka</string>
    <key>CFBundleName</key><string>Vstavka</string>
    <key>CFBundleDisplayName</key><string>Vstavka</string>
    <key>CFBundleExecutable</key><string>$executable</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
$icon_key
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$bundle"
codesign --verify --deep "$bundle"
echo "$bundle"
