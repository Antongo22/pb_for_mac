#!/usr/bin/env bash
# Собирает готовый к распространению пакет PbForMac для указанной платформы.
#   macOS:   artifacts/PbForMac-<rid>.zip     (PbForMac.app)
#   Windows: artifacts/PbForMac-<rid>.zip     (app/ + install.cmd / install.ps1)
#   Linux:   artifacts/PbForMac-<rid>.tar.gz  (app/ + install.sh + ярлык)
# Использование: scripts/package.sh <osx-arm64|osx-x64|win-x64|win-arm64|linux-x64|linux-arm64>
set -euo pipefail

RID="${1:?Укажите RID, например osx-arm64, win-x64 или linux-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARTIFACTS="$ROOT/artifacts"
NAME="PbForMac-$RID"
STAGE="$ARTIFACTS/$NAME"
mkdir -p "$ARTIFACTS"

publish() {
  dotnet publish "$ROOT/src/PbForMac/PbForMac.csproj" -c Release -r "$RID" --self-contained true -o "$1"
}

case "$RID" in
  osx-*)
    "$ROOT/scripts/publish-macos.sh" "$RID"
    rm -f "$ARTIFACTS/$NAME.zip"
    (cd "$ARTIFACTS/$RID" && ditto -c -k --keepParent PbForMac.app "$ARTIFACTS/$NAME.zip")
    echo "Готово: $ARTIFACTS/$NAME.zip"
    ;;

  win-*)
    rm -rf "$STAGE" "$ARTIFACTS/$NAME.zip"
    publish "$STAGE/app"
    cp "$ROOT/packaging/windows/"{install.ps1,install.cmd,uninstall.cmd} "$STAGE/"
    if command -v zip >/dev/null; then
      (cd "$ARTIFACTS" && zip -qr "$NAME.zip" "$NAME")
    elif command -v 7z >/dev/null; then
      (cd "$ARTIFACTS" && 7z a -tzip -bso0 "$NAME.zip" "$NAME")
    else
      powershell -NoProfile -Command "Compress-Archive -Path '$STAGE' -DestinationPath '$ARTIFACTS/$NAME.zip'"
    fi
    echo "Готово: $ARTIFACTS/$NAME.zip"
    ;;

  linux-*)
    rm -rf "$STAGE" "$ARTIFACTS/$NAME.tar.gz"
    publish "$STAGE/app"
    cp "$ROOT/packaging/linux/"{install.sh,pbformac.desktop,pbformac-mime.xml} "$STAGE/"
    cp "$ROOT/src/PbForMac/Assets/app.png" "$STAGE/pbformac.png"
    chmod +x "$STAGE/install.sh" "$STAGE/app/PbForMac"
    tar -C "$ARTIFACTS" -czf "$ARTIFACTS/$NAME.tar.gz" "$NAME"
    echo "Готово: $ARTIFACTS/$NAME.tar.gz"
    ;;

  *)
    echo "Неизвестный RID: $RID" >&2
    exit 1
    ;;
esac
