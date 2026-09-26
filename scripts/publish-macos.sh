#!/usr/bin/env bash
# Собирает PbForMac.app (self-contained) для macOS.
# Использование: scripts/publish-macos.sh [osx-arm64|osx-x64]
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/$RID"
APP="$OUT/PbForMac.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"

rm -rf "$OUT"
dotnet publish "$ROOT/src/PbForMac/PbForMac.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -o "$OUT/bin"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/bin/." "$APP/Contents/MacOS/"

# Иконка .icns из PNG 1024×1024.
ICONSET="$OUT/PbForMac.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$ROOT/src/PbForMac/Assets/app.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$ROOT/src/PbForMac/Assets/app.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/PbForMac.icns"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>PbForMac</string>
  <key>CFBundleDisplayName</key><string>PbForMac</string>
  <key>CFBundleIdentifier</key><string>io.github.antongo22.pbformac</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>PbForMac</string>
  <key>CFBundleIconFile</key><string>PbForMac.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>PbForMac Report</string>
      <key>CFBundleTypeExtensions</key><array><string>pbm</string></array>
      <key>CFBundleTypeRole</key><string>Editor</string>
    </dict>
  </array>
</dict>
</plist>
PLIST

# Ad-hoc подпись, чтобы приложение запускалось локально на Apple Silicon.
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
rm -rf "$OUT/bin"
echo "Готово: $APP"
