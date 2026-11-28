#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
version="${1:-$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' macos/Info.plist)}"
version="${version#v}"
if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+){1,2}$ ]]; then
  echo "Invalid version: $version" >&2
  exit 1
fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
app="$work/WheekKeys.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" dist
cp macos/Info.plist "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app/Contents/Info.plist"
cp assets/avatar.png assets/pixel/{guinea-pig-*,mouth-*}.png assets/pixel/coats/*.png assets/pixel/doze.txt "$app/Contents/Resources/"
cp assets/qq-group.png "$app/Contents/Resources/"
cp assets/pixel/paw.png "$app/Contents/Resources/"
cp assets/pixel/mac-keyboard.png "$app/Contents/Resources/keyboard.png"
cp assets/pixel/mac-mouse.png "$app/Contents/Resources/mouse.png"
cp assets/pixel/keyboard.png "$app/Contents/Resources/reference-keyboard.png"
cp assets/pixel/mouse.png "$app/Contents/Resources/reference-mouse.png"
cp assets/pixel/{keyboard,mouse}-{peach,mocha}.png "$app/Contents/Resources/"
for file in assets/drawn/*.png; do cp "$file" "$app/Contents/Resources/drawn-$(basename "$file")"; done
cp assets/keyboard-layout.txt "$app/Contents/Resources/keyboard-layout.txt"
mkdir "$work/WheekKeys.iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" assets/avatar.png --out "$work/WheekKeys.iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" assets/avatar.png --out "$work/WheekKeys.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$work/WheekKeys.iconset" -o "$app/Contents/Resources/WheekKeys.icns"
for arch in arm64 x86_64; do
  xcrun swiftc -import-objc-header macos/WheekArchive.h -swift-version 5 -parse-as-library -O -target "$arch-apple-macos11.0" \
    macos/main.swift macos/AnonymousKeyboardInput.swift macos/PetSkin.swift macos/WheekPackLoader.swift macos/PetView.swift macos/PetSettings.swift macos/FriendDesktop.swift \
    macos/LocalPeers.swift macos/RoomTransport.swift macos/RoomMembership.swift macos/RoomDirectory.swift \
    macos/NetworkKeyCodes.swift macos/SmokeTest.swift macos/WheekPackSmokeTest.swift -larchive -framework IOKit -framework Carbon -o "$work/WheekKeys-$arch"
done
lipo -create "$work/WheekKeys-arm64" "$work/WheekKeys-x86_64" -output "$app/Contents/MacOS/WheekKeys"
chmod 755 "$app/Contents/MacOS/WheekKeys"
codesign --force --sign - "$app"
codesign --verify --deep --strict --verbose=2 "$app"
lipo "$app/Contents/MacOS/WheekKeys" -verify_arch arm64 x86_64
rm -f dist/WheekKeys-macOS.zip
ditto -c -k --keepParent "$app" dist/WheekKeys-macOS.zip
