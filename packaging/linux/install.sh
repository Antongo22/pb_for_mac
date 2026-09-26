#!/usr/bin/env bash
# Устанавливает PbForMac для текущего пользователя (без sudo):
#   приложение → ~/.local/share/pbformac, команда → ~/.local/bin/pbformac,
#   ярлык в меню приложений и ассоциация файлов отчётов .pbm.
# Удаление: ./install.sh --uninstall
# Другой каталог установки: PREFIX=/opt/pbformac ./install.sh (потребуются права записи)
set -euo pipefail

PREFIX="${PREFIX:-$HOME/.local}"
APP_DIR="$PREFIX/share/pbformac"
BIN="$PREFIX/bin/pbformac"
DESKTOP="$PREFIX/share/applications/pbformac.desktop"
MIME="$PREFIX/share/mime/packages/pbformac.xml"
SRC="$(cd "$(dirname "$0")" && pwd)"

refresh_caches() {
  command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" >/dev/null 2>&1 || true
  command -v update-mime-database >/dev/null && update-mime-database "$PREFIX/share/mime" >/dev/null 2>&1 || true
}

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$APP_DIR"
  rm -f "$BIN" "$DESKTOP" "$MIME"
  refresh_caches
  echo "PbForMac удалён."
  exit 0
fi

mkdir -p "$APP_DIR" "$(dirname "$BIN")" "$(dirname "$DESKTOP")" "$(dirname "$MIME")"
rm -rf "${APP_DIR:?}/"*
cp -R "$SRC/app/." "$APP_DIR/"
cp "$SRC/pbformac.png" "$APP_DIR/pbformac.png"
chmod +x "$APP_DIR/PbForMac"

cat > "$BIN" <<EOF
#!/usr/bin/env bash
exec "$APP_DIR/PbForMac" "\$@"
EOF
chmod +x "$BIN"

sed -e "s|@EXEC@|$BIN|g" -e "s|@ICON@|$APP_DIR/pbformac.png|g" "$SRC/pbformac.desktop" > "$DESKTOP"
cp "$SRC/pbformac-mime.xml" "$MIME"
refresh_caches

echo "PbForMac установлен в $APP_DIR"
echo "Запуск: из меню приложений или командой «pbformac»."
case ":$PATH:" in
  *":$PREFIX/bin:"*) ;;
  *) echo "Примечание: добавьте $PREFIX/bin в PATH, чтобы запускать команду pbformac из терминала." ;;
esac
