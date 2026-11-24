#!/usr/bin/env bash
set -euo pipefail

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
output=${1:-"$repo/artifacts/gnome-shell"}
mkdir -p -- "$output"
output=$(cd -- "$output" && pwd)
source="$repo/gnome-extension/vstavka@xopoko.github.io"
work=$(mktemp -d)
trap 'rm -r -- "$work"' EXIT

cp -- "$source"/{clipboard,extension-core,snap-peer,windows}.js "$source/shell.xml" "$repo/LICENSE" "$work/"
cp -- "$repo/gnome-extension/snap-extension.js" "$work/extension.js"
cp -- "$repo/gnome-extension/snap-metadata.json" "$work/metadata.json"
(
    cd -- "$work"
    zip -q vstavka-snap@xopoko.github.io.zip -- *.js LICENSE metadata.json shell.xml
)
mv -- "$work/vstavka-snap@xopoko.github.io.zip" "$output/"
printf '%s\n' "$output/vstavka-snap@xopoko.github.io.zip"
