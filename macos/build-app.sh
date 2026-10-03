#!/usr/bin/env bash
# Builds "build/Window Overlay.app" from the Swift package.
#   bash build-app.sh             build for this Mac's CPU
#   UNIVERSAL=1 bash build-app.sh build for Apple silicon + Intel (needs full Xcode, not just the CLI tools)
set -euo pipefail
cd "$(dirname "$0")"

if [[ "${UNIVERSAL:-0}" == "1" ]]; then
  swift build -c release --arch arm64 --arch x86_64
  bin_dir="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"
else
  swift build -c release
  bin_dir="$(swift build -c release --show-bin-path)"
fi

app="build/Window Overlay.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$bin_dir/WindowOverlay" "$app/Contents/MacOS/WindowOverlay"
cp Resources/Info.plist "$app/Contents/Info.plist"

# App icon: render every size macOS wants from the 1024 px PNG.
iconset="build/AppIcon.iconset"
rm -rf "$iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" Resources/AppIcon.png --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) Resources/AppIcon.png --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
rm -rf "$iconset"

# Ad-hoc signature so macOS can track the Screen Recording permission for this app.
codesign --force --deep --sign - "$app"

echo "Built: $(pwd)/$app"
echo "Run it with: open \"$app\"   (or drag it into /Applications)"
