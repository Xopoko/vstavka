#!/bin/bash
# Assembles Vstavka.app on macOS from a self-contained osx-arm64 or osx-x64 publish of
# src/Vstavka.Desktop. The publish can come from any OS; this step needs only macOS tools.
#
#   dotnet publish src/Vstavka.Desktop -c Release -r osx-arm64 --self-contained -o <publish-dir>
#   scripts/package-macos-app.sh <publish-dir> <output-dir> [version]
#
# For the Mac App Store, publish with -p:VstavkaMacAppStore=true -p:PublishAot=true, then run:
#   scripts/package-macos-app.sh <publish-dir> <output-dir> <version> <build> --app-store
# Set VSTAVKA_MACOS_APP_IDENTITY and VSTAVKA_MACOS_INSTALLER_IDENTITY to the signing
# identities and VSTAVKA_MACOS_PROFILE to the Mac App Store provisioning profile.
# Use --sandbox-test in place of --app-store for a local sandboxed .app.
set -euo pipefail

if [[ "$(uname)" != "Darwin" ]]; then
    echo "Run this script on macOS." >&2
    exit 1
fi
if [[ $# -lt 2 || $# -gt 5 ]]; then
    echo "Usage: $0 <publish-dir> <output-dir> [version [build [--app-store|--sandbox-test]]]" >&2
    exit 1
fi

publish_dir="$(cd "$1" && pwd)"
output_dir="$2"
version="${3:-0.3.0}"
build="${4:-$version}"
mode="${5:-}"
script_dir="$(cd "$(dirname "$0")" && pwd)"
icon_source="$script_dir/../assets/Vstavka.png"
executable="Vstavka.Desktop"
app_identity="-"
case "$mode" in
    --app-store)
        app_identity="${VSTAVKA_MACOS_APP_IDENTITY:?Set the Apple Distribution signing identity}"
        : "${VSTAVKA_MACOS_INSTALLER_IDENTITY:?Set the Mac App Store installer signing identity}"
        : "${VSTAVKA_MACOS_PROFILE:?Set the Mac App Store provisioning profile}"
        umask 022
        ;;
    --sandbox-test|"") ;;
    *) echo "Unknown packaging mode: $mode" >&2; exit 1 ;;
esac
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || ! "$build" =~ ^[0-9]+(\.[0-9]+){0,2}$ ]]; then
    echo "Specify a major.minor.patch version and a numeric build number." >&2
    exit 1
fi

if [[ ! -f "$publish_dir/$executable" ]]; then
    echo "$publish_dir does not contain $executable." >&2
    exit 1
fi
if [[ -n "$mode" && -f "$publish_dir/$executable.dll" ]]; then
    echo "The App Store build requires a Native AOT publish in a fresh directory." >&2
    exit 1
fi

mkdir -p "$output_dir"
bundle="$(cd "$output_dir" && pwd)/Vstavka.app"
package="${bundle%.app}.pkg"
if [[ -e "$bundle" || ( "$mode" == --app-store && -e "$package" ) ]]; then
    echo "The output already exists. Use a new output directory." >&2
    exit 1
fi

mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
macos="$bundle/Contents/MacOS"
resources="$bundle/Contents/Resources"
minimum_macos="13.0"
if [[ -z "$mode" ]]; then
    cp -R "$publish_dir/." "$macos/"
else
    minimum_macos="14.0"
    # Native AOT resolves the UI libraries relative to the executable.
    # Keep the signed libraries in Frameworks and expose them to that loader.
    mkdir -p "$bundle/Contents/Frameworks"
    shopt -s nullglob
    cp "$publish_dir/$executable" "$macos/"
    for library in "$publish_dir"/*.dylib; do
        name="${library##*/}"
        cp "$library" "$bundle/Contents/Frameworks/$name"
        ln -s "../Frameworks/$name" "$macos/$name"
    done
    cp "$script_dir/../LICENSE" "$resources/License.txt"
    cp "$script_dir/../packaging/MacThirdPartyNotices.txt" "$resources/ThirdPartyNotices.txt"
fi
chmod +x "$bundle/Contents/MacOS/$executable"

icon_key=""
if [[ -f "$icon_source" ]] && command -v iconutil >/dev/null && command -v sips >/dev/null; then
    icon_temp="$(mktemp -d)"
    trap 'rm -rf "$icon_temp"' EXIT
    iconset="$icon_temp/Vstavka.iconset"
    mkdir -p "$iconset"
    for size in 16 32 128 256 512; do
        sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
        sips -z "$((size * 2))" "$((size * 2))" "$icon_source" \
            --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/Vstavka.icns"
    icon_key="    <key>CFBundleIconFile</key><string>Vstavka.icns</string>"
elif [[ -n "$mode" ]]; then
    echo "The app icon is required for App Store packaging." >&2
    exit 1
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
    <key>CFBundleVersion</key><string>$build</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
    <key>LSMinimumSystemVersion</key><string>$minimum_macos</string>
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
$icon_key
</dict>
</plist>
PLIST

if [[ -n "$mode" ]]; then
    # On macOS the history encryption uses the system CryptoKit implementation.
    /usr/libexec/PlistBuddy -c 'Add :ITSAppUsesNonExemptEncryption bool false' "$bundle/Contents/Info.plist"
fi

if [[ -z "$mode" ]]; then
    codesign --force --deep --sign - "$bundle"
else
    sign_options=()
    keychain_options=()
    entitlements="$script_dir/../packaging/MacAppStore.entitlements"
    if [[ "$mode" == --app-store ]]; then
        sign_options=(--timestamp --options runtime)
        if [[ -n "${VSTAVKA_SIGNING_KEYCHAIN:-}" ]]; then
            keychain_options=(--keychain "$VSTAVKA_SIGNING_KEYCHAIN")
        fi

        cp "$VSTAVKA_MACOS_PROFILE" "$bundle/Contents/embedded.provisionprofile"
        profile_plist="$icon_temp/profile.plist"
        security cms -D -i "$VSTAVKA_MACOS_PROFILE" -o "$profile_plist"
        app_id="$(/usr/libexec/PlistBuddy -c 'Print :Entitlements:com.apple.application-identifier' "$profile_plist")"
        team_id="$(/usr/libexec/PlistBuddy -c 'Print :TeamIdentifier:0' "$profile_plist")"
        if [[ "$app_id" != *.io.github.xopoko.Vstavka ]]; then
            echo "The provisioning profile does not match the Vstavka bundle ID." >&2
            exit 1
        fi
        cp "$entitlements" "$icon_temp/distribution.entitlements"
        entitlements="$icon_temp/distribution.entitlements"
        /usr/libexec/PlistBuddy -c "Add :com.apple.application-identifier string $app_id" "$entitlements"
        /usr/libexec/PlistBuddy -c "Add :com.apple.developer.team-identifier string $team_id" "$entitlements"
        chmod -R a+rX "$bundle"
    fi
    for library in "$bundle/Contents/Frameworks"/*.dylib; do
        codesign --force --sign "$app_identity" ${keychain_options[@]+"${keychain_options[@]}"} \
            ${sign_options[@]+"${sign_options[@]}"} "$library"
    done
    codesign --force --sign "$app_identity" ${keychain_options[@]+"${keychain_options[@]}"} \
        ${sign_options[@]+"${sign_options[@]}"} --entitlements "$entitlements" "$bundle"
fi
codesign --verify --strict --deep "$bundle"
if [[ "$mode" == --app-store ]]; then
    productbuild --component "$bundle" /Applications ${keychain_options[@]+"${keychain_options[@]}"} \
        --sign "$VSTAVKA_MACOS_INSTALLER_IDENTITY" "$package"
    pkgutil --check-signature "$package"
    echo "$package"
else
    echo "$bundle"
fi
