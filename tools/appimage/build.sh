#!/usr/bin/env bash
# Packs the Linux launcher into one AppImage that runs on any x86_64 distro (and the Steam Deck).
#   tools/appimage/build.sh <publish dir> <version> <output.AppImage>
# <publish dir> = dotnet publish src/SubnauticaMP.Launcher -c Release -r linux-x64 -o <dir>
set -euo pipefail
PUB=$(realpath "$1"); VERSION=$2; OUT=$(realpath -m "$3")
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
WORK=$(mktemp -d)
APPDIR="$WORK/SubnauticaMP.AppDir"

mkdir -p "$APPDIR/usr/bin"
cp -r "$PUB"/. "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/SubnauticaMP-Launcher"

cat > "$APPDIR/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/SubnauticaMP-Launcher" "$@"
RUN
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/subnautica-multiplayer.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=Subnautica Multiplayer
Comment=Launcher for the Subnautica multiplayer mod
Exec=SubnauticaMP-Launcher
Icon=subnautica-multiplayer
Categories=Game;
Terminal=false
X-AppImage-Version=$VERSION
DESK

cp "$ROOT/src/SubnauticaMP.Launcher/Assets/icon.png" "$APPDIR/subnautica-multiplayer.png"
cp "$ROOT/src/SubnauticaMP.Launcher/Assets/icon.png" "$APPDIR/.DirIcon"

TOOL="$WORK/appimagetool"
curl -fsSL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"
# --appimage-extract-and-run: works without FUSE (CI containers)
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$APPDIR" "$OUT"
rm -rf "$WORK"
echo "Built $OUT"
