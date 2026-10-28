#!/bin/sh
set -e
cd "$(dirname "$0")"

PREFIX="${PREFIX:-$HOME/.local}"
APP_DIR="$PREFIX/share/zapret-gui/app"

mkdir -p "$APP_DIR" "$PREFIX/bin" "$PREFIX/share/applications" "$PREFIX/share/icons/hicolor/256x256/apps"
cp -f ZapretGUI "$APP_DIR/ZapretGUI"
chmod 755 "$APP_DIR/ZapretGUI"
if [ -d zapret ]; then
  rm -rf "$APP_DIR/zapret"
  cp -r zapret "$APP_DIR/zapret"
fi
cp -f zapret-gui.png "$PREFIX/share/icons/hicolor/256x256/apps/zapret-gui.png"
printf '#!/bin/sh\nexec "%s" "$@"\n' "$APP_DIR/ZapretGUI" > "$PREFIX/bin/zapret-gui"
chmod 755 "$PREFIX/bin/zapret-gui"

cat > "$PREFIX/share/applications/zapret-gui.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Zapret GUI
Comment=Обход блокировок Discord и YouTube
Exec=$APP_DIR/ZapretGUI
Icon=zapret-gui
Terminal=false
Categories=Network;
EOF

echo "Zapret GUI установлен в $APP_DIR"
echo "Запуск: zapret-gui или через меню приложений"
