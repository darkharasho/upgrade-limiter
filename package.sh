#!/usr/bin/env bash
set -euo pipefail

VERSION="$(python3 -c "import json; print(json.load(open('manifest.json'))['version_number'])")"
DLL="bin/Release/netstandard2.1/UpgradeLimiter.dll"
OUT="UpgradeLimiter-${VERSION}.zip"
R2_PROFILE="${R2_PROFILE:-Default}"
R2_PLUGINS="$HOME/.config/r2modmanPlus-local/REPO/profiles/$R2_PROFILE/BepInEx/plugins/UpgradeLimiter"

if [ -z "${GAME_DIR:-}" ]; then
    for candidate in \
        "$HOME/.steam/steam/steamapps/common/REPO" \
        "$HOME/.local/share/Steam/steamapps/common/REPO"
    do
        if [ -d "$candidate" ]; then
            GAME_DIR="$candidate"
            break
        fi
    done
fi

if [ -z "${GAME_DIR:-}" ]; then
    echo "ERROR: Could not find R.E.P.O. install. Set GAME_DIR manually:"
    echo "  GAME_DIR=\"/path/to/REPO\" ./package.sh"
    exit 1
fi

echo "Using game dir: $GAME_DIR"

dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"

mkdir -p "$R2_PLUGINS"
cp "$DLL" "$R2_PLUGINS/"
echo "Deployed to r2modman profile: $R2_PROFILE"

if [ ! -f "icon.png" ]; then
    echo "WARNING: icon.png not found — skipping Thunderstore zip."
    exit 0
fi

rm -f "$OUT"
zip -j "$OUT" manifest.json icon.png README.md "$DLL"
echo "Packaged: $OUT"
