#!/usr/bin/env bash
# Собирает PbForMac из исходников и устанавливает его (macOS или Linux).
#   macOS: PbForMac.app → /Applications (или ~/Applications, если нет прав записи)
#   Linux: ~/.local (меню приложений, команда pbformac, файлы .pbm) — см. packaging/linux/install.sh
# Использование: scripts/install.sh            — собрать и установить
#                scripts/install.sh --uninstall — удалить
# Нужен .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
case "$(uname -m)" in
  arm64 | aarch64) ARCH=arm64 ;;
  x86_64 | amd64) ARCH=x64 ;;
  *) echo "Неподдерживаемая архитектура: $(uname -m)" >&2; exit 1 ;;
esac

uninstall=false
[[ "${1:-}" == "--uninstall" ]] && uninstall=true

case "$(uname -s)" in
  Darwin)
    if $uninstall; then
      rm -rf "/Applications/PbForMac.app" "$HOME/Applications/PbForMac.app"
      echo "PbForMac удалён."
      exit 0
    fi
    command -v dotnet >/dev/null || { echo "Нужен .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2; exit 1; }
    RID="osx-$ARCH"
    "$ROOT/scripts/publish-macos.sh" "$RID"
    DEST="${DEST:-/Applications}"
    if [[ ! -w "$DEST" ]]; then
      DEST="$HOME/Applications"
      mkdir -p "$DEST"
    fi
    rm -rf "$DEST/PbForMac.app"
    cp -R "$ROOT/artifacts/$RID/PbForMac.app" "$DEST/"
    echo "PbForMac установлен: $DEST/PbForMac.app"
    echo "Запуск: open \"$DEST/PbForMac.app\" или через Launchpad / Spotlight."
    ;;

  Linux)
    if $uninstall; then
      "$ROOT/packaging/linux/install.sh" --uninstall
      exit 0
    fi
    command -v dotnet >/dev/null || { echo "Нужен .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2; exit 1; }
    RID="linux-$ARCH"
    "$ROOT/scripts/package.sh" "$RID"
    "$ROOT/artifacts/PbForMac-$RID/install.sh"
    ;;

  *)
    echo "Для Windows используйте scripts\\install.ps1" >&2
    exit 1
    ;;
esac
