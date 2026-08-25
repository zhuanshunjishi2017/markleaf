#!/usr/bin/env bash

# Create a branded drag-install DMG while keeping Finder metadata and its
# background alias bound to the same filesystem that ships to users.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fail() {
    echo "[create-dmg] error: $*" >&2
    exit 1
}

[ "$#" -eq 3 ] || fail "Usage: $0 <MarkLeaf.app> <output.dmg> <volume-name>"

SOURCE_APP="$1"
OUTPUT_DMG="$2"
VOLUME_NAME="$3"
SETUP_VOLUME_NAME="MarkLeaf Layout $(date +%s)-$$"

[ -d "$SOURCE_APP" ] || fail "Application bundle does not exist: $SOURCE_APP"
mkdir -p "$(dirname "$OUTPUT_DMG")"

BUILD_ROOT="$(mktemp -d /private/tmp/markleaf-branded-dmg.XXXXXX)"
STAGE_DIR="$BUILD_ROOT/stage"
MOUNT_DIR="$(mktemp -d /private/tmp/markleaf-dmg-volume.XXXXXX)"
RW_DMG="$BUILD_ROOT/MarkLeaf-rw.dmg"
ATTACHED=0
APP_SIZE_KB="$(du -sk "$SOURCE_APP" | awk '{print $1}')"
DMG_SIZE_KB="$((APP_SIZE_KB + 65536))"

cleanup() {
    if [ "$ATTACHED" = 1 ]; then
        hdiutil detach "$MOUNT_DIR" >/dev/null 2>&1 || true
    fi
    rmdir "$MOUNT_DIR" >/dev/null 2>&1 || rm -rf "$MOUNT_DIR"
    rm -rf "$BUILD_ROOT"
}
trap cleanup EXIT

mkdir -p "$STAGE_DIR/MarkLeaf.app"
ln -s /Applications "$STAGE_DIR/Applications"
bash "$HERE/markleaf-dmg-layout.sh" prepare "$STAGE_DIR"

hdiutil create \
    -volname "$SETUP_VOLUME_NAME" \
    -size "${DMG_SIZE_KB}k" \
    -srcfolder "$STAGE_DIR" \
    -ov \
    -format UDRW \
    "$RW_DMG" >/dev/null

hdiutil attach \
    -readwrite \
    -noverify \
    -noautoopen \
    -mountpoint "$MOUNT_DIR" \
    "$RW_DMG" >/dev/null
ATTACHED=1

bash "$HERE/markleaf-dmg-layout.sh" apply "$MOUNT_DIR"

# The Finder layout now belongs to this exact volume. Populate the real app
# only after Finder has persisted the background alias and icon positions.
ditto "$SOURCE_APP" "$MOUNT_DIR/MarkLeaf.app"
xattr -cr "$MOUNT_DIR/MarkLeaf.app"

# Finder may cache layout state by volume name. Generate the layout under a
# unique internal name, then rename this same filesystem for the user-facing
# installer without invalidating the background file alias.
MOUNT_DEVICE="$(df "$MOUNT_DIR" | awk 'NR == 2 { print $1 }')"
[ -n "$MOUNT_DEVICE" ] || fail "Could not resolve mounted DMG device"
diskutil rename "$MOUNT_DEVICE" "$VOLUME_NAME" >/dev/null
sync

hdiutil detach "$MOUNT_DIR" >/dev/null
ATTACHED=0

rm -f "$OUTPUT_DMG"
hdiutil convert "$RW_DMG" -format UDZO -ov -o "$OUTPUT_DMG" >/dev/null
hdiutil verify "$OUTPUT_DMG" >/dev/null

echo "[create-dmg] completed branded DMG: $OUTPUT_DMG"
