#!/usr/bin/env bash

# Stage and apply the branded Finder layout for the MarkLeaf DMG.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKGROUND_NAME="MarkLeaf-dmg-background.png"
BACKGROUND_SOURCE="$HERE/dmg-assets/$BACKGROUND_NAME"

usage() {
    echo "Usage: $0 prepare <stage-dir> | apply <mounted-volume>" >&2
    exit 2
}

fail() {
    echo "[dmg-layout] error: $*" >&2
    exit 1
}

warn() {
    echo "[dmg-layout] warning: $*" >&2
}

[ "$#" -eq 2 ] || usage
ACTION="$1"
DMG_ROOT="$2"
[ -d "$DMG_ROOT" ] || fail "DMG directory does not exist: $DMG_ROOT"

case "$ACTION" in
    prepare)
        [ -f "$BACKGROUND_SOURCE" ] || fail "Missing DMG background: $BACKGROUND_SOURCE"
        mkdir -p "$DMG_ROOT/.background"
        cp "$BACKGROUND_SOURCE" "$DMG_ROOT/.background/$BACKGROUND_NAME"
        xattr -c "$DMG_ROOT/.background/$BACKGROUND_NAME" 2>/dev/null || true
        chflags hidden "$DMG_ROOT/.background" 2>/dev/null || true
        echo '[dmg-layout] staged branded DMG background'
        ;;
    apply)
        OSASCRIPT_BIN="${OSASCRIPT_BIN:-/usr/bin/osascript}"
        if [ ! -x "$OSASCRIPT_BIN" ]; then
            fail 'Finder layout tool unavailable; refusing to create an unbranded DMG'
        fi

        MOUNTED_PATH="$(cd "$DMG_ROOT" && pwd)"
        echo "[dmg-layout] applying Finder layout at $MOUNTED_PATH"
        if ! "$OSASCRIPT_BIN" - "$MOUNTED_PATH" <<'APPLESCRIPT'
on run argv
    set mountedPath to item 1 of argv
    tell application "Finder"
        set targetDisk to disk (POSIX file mountedPath as alias)
        try
            set oldWindow to container window of targetDisk
            close oldWindow
        end try
        open targetDisk
        delay 4
        set targetWindow to container window of targetDisk
        set current view of targetWindow to icon view
        set toolbar visible of targetWindow to false
        set statusbar visible of targetWindow to false
        set pathbar visible of targetWindow to false
        set bounds of targetWindow to {100, 100, 740, 500}
        set viewOptions to icon view options of targetWindow
        set arrangement of viewOptions to not arranged
        set icon size of viewOptions to 112
        set text size of viewOptions to 13
        set backgroundFile to (POSIX file (mountedPath & "/.background/MarkLeaf-dmg-background.png")) as alias
        set background picture of viewOptions to backgroundFile
        delay 3
        set position of item "MarkLeaf.app" of targetDisk to {165, 220}
        set position of item "Applications" of targetDisk to {475, 220}
        update targetDisk without registering applications
        delay 2
        close targetWindow
    end tell
end run
APPLESCRIPT
        then
            fail 'Finder layout could not be applied; refusing to create an unbranded DMG'
        fi
        echo '[dmg-layout] applied branded Finder layout'
        ;;
    *)
        usage
        ;;
esac
