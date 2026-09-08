#!/usr/bin/env bash
# Build the InstaChat Linux .deb package.
#
#   ./linux/build-deb.sh
#
# Requires: a .NET 10 SDK (auto-detected; override with DOTNET=/path/to/dotnet)
# and Python 3. Works from the Windows checkout too (no dpkg needed - the
# .deb is assembled by tools/mkdeb.py).
set -euo pipefail

cd "$(dirname "$0")/.."   # repository root

# ---------------------------------------------------------------------------
# Locate the .NET SDK: explicit override, workspace-local SDK, then PATH.
# ---------------------------------------------------------------------------
DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
    for candidate in \
        "../tools/dotnet-sdk/dotnet" \
        "../tools/dotnet-sdk/dotnet.exe" \
        "$(command -v dotnet 2>/dev/null || true)"; do
        if [ -n "$candidate" ] && [ -x "$candidate" ]; then
            if "$candidate" --list-sdks >/dev/null 2>&1; then
                DOTNET="$candidate"
                break
            fi
        fi
    done
fi
if [ -z "$DOTNET" ]; then
    echo "error: no working dotnet SDK found. Install .NET 10 or set DOTNET=/path/to/dotnet" >&2
    exit 1
fi
echo "Using dotnet: $DOTNET"

VERSION_LABEL="1.0-Linux-beta1"
DEBIAN_VERSION="1.0.0~beta1"
RID="${RID:-linux-x64}"
ARCH="amd64"
PROJECT="src/InstaChatLinux/InstaChatLinux.csproj"
PUBLISH_DIR="src/InstaChatLinux/bin/Release/net10.0/${RID}/publish"
STAGING="linux/staging"
OUT="dist/InstaChat-${VERSION_LABEL}-${ARCH}.deb"

echo "==> Publishing ${RID} (self-contained)..."
"$DOTNET" publish "$PROJECT" -c Release -r "$RID" --self-contained true -p:DebugType=none -p:DebugSymbols=false

echo "==> Assembling package layout..."
rm -rf "$STAGING"
mkdir -p "$STAGING/DEBIAN"
mkdir -p "$STAGING/usr/bin"
mkdir -p "$STAGING/usr/lib/instachat"
mkdir -p "$STAGING/usr/share/applications"
mkdir -p "$STAGING/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$STAGING/usr/share/doc/instachat"

cp -r "$PUBLISH_DIR"/. "$STAGING/usr/lib/instachat/"
# A real copy (not a symlink) keeps the package portable across filesystems
# and hosts - MSYS on Windows turns `ln -s` into a plain copy anyway.
cp "$STAGING/usr/lib/instachat/InstaChat" "$STAGING/usr/bin/instachat"
cp linux/com.instachat.InstaChat.desktop "$STAGING/usr/share/applications/"
cp linux/icon/instachat-256.png "$STAGING/usr/share/icons/hicolor/256x256/apps/instachat.png"
cp linux/copyright "$STAGING/usr/share/doc/instachat/copyright"

SIZE_KB=$(du -sk "$STAGING/usr" | cut -f1)
sed -e "s/@VERSION@/${DEBIAN_VERSION}/" \
    -e "s/@ARCH@/${ARCH}/" \
    -e "s/@SIZE@/${SIZE_KB}/" \
    linux/debian/control > "$STAGING/DEBIAN/control"

cp linux/debian/postinst linux/debian/postrm "$STAGING/DEBIAN/"
chmod 0755 "$STAGING/DEBIAN/postinst" "$STAGING/DEBIAN/postrm"

echo "==> Building .deb..."
mkdir -p dist
PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
    if command -v python3 >/dev/null 2>&1; then PYTHON=python3; else PYTHON=python; fi
fi
"$PYTHON" tools/mkdeb.py "$STAGING" "$OUT"
rm -rf "$STAGING"

echo "==> Done: $OUT"
echo "    Install with: sudo apt install ./${OUT##*/}"