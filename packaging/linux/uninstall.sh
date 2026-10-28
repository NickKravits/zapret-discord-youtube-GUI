#!/bin/sh
PREFIX="${PREFIX:-$HOME/.local}"

rm -rf "$PREFIX/share/zapret-gui/app"
rm -f "$PREFIX/bin/zapret-gui" "$PREFIX/share/applications/zapret-gui.desktop" \
      "$PREFIX/share/icons/hicolor/256x256/apps/zapret-gui.png" "$HOME/.config/autostart/zapret-gui.desktop"

if [ -f /etc/systemd/system/zapret-gui.service ] || [ -d /opt/zapret-gui ]; then
  echo "Удаление службы и системных файлов (нужен пароль администратора)…"
  sudo sh -c 'systemctl disable --now zapret-gui.service 2>/dev/null; rm -f /etc/systemd/system/zapret-gui.service; systemctl daemon-reload; nft delete table inet zapretgui 2>/dev/null; rm -rf /opt/zapret-gui'
fi

echo "Zapret GUI удалён. Настройки и списки остались в ~/.local/share/zapret-gui"
