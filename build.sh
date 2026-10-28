#!/bin/sh
set -e
cd "$(dirname "$0")"
RID="${1:-linux-x64}"
dotnet publish src/ZapretGui/ZapretGui.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
  -o "dist/$RID"
chmod +x "dist/$RID/ZapretGUI"
echo "Готово: dist/$RID/ZapretGUI"
