#!/usr/bin/env bash
#
# Builds the AxoPrint Linux client as an AppImage. Run this ON a Linux machine
# with the .NET 10 SDK installed (e.g. your server, or any Linux desktop):
#
#   bash deploy/appimage/build-appimage.sh
#
# Output: build/AxoPrint-x86_64.AppImage
#
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
BUILD="$ROOT/build"
APPDIR="$BUILD/AppDir"

echo "==> Publishing linux-x64 (self-contained)"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
dotnet publish "$ROOT/src/AxoPrint.LinuxClient" -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=false -o "$APPDIR/usr/bin"

echo "==> Assembling AppDir"
install -m 0755 "$HERE/AppRun" "$APPDIR/AppRun"
install -m 0644 "$HERE/axoprint.desktop" "$APPDIR/axoprint.desktop"
install -m 0644 "$HERE/axoprint.png" "$APPDIR/axoprint.png"
mkdir -p "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp "$HERE/axoprint.desktop" "$APPDIR/usr/share/applications/"
cp "$HERE/axoprint.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/"

echo "==> Fetching appimagetool"
TOOL="$BUILD/appimagetool-x86_64.AppImage"
if [ ! -x "$TOOL" ]; then
    curl -fsSL -o "$TOOL" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$TOOL"
fi

echo "==> Building AppImage"
# --appimage-extract-and-run avoids needing FUSE on the build host.
ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$APPDIR" "$BUILD/AxoPrint-x86_64.AppImage"

echo "==> Done: $BUILD/AxoPrint-x86_64.AppImage"
