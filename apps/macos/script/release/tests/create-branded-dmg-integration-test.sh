#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="$(cd "$HERE/.." && pwd)"
CREATOR="$RELEASE_DIR/create-branded-dmg.sh"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

[ -x "$CREATOR" ] || fail "Missing executable branded DMG creator: $CREATOR"

TEST_ROOT="$(mktemp -d /private/tmp/markleaf-branded-dmg-test.XXXXXX)"
SOURCE_APP="$TEST_ROOT/SourceMarkLeaf.app"
OUTPUT_DMG="$TEST_ROOT/MarkLeaf-test.dmg"
MOUNT_DIR="$(mktemp -d /private/tmp/markleaf-branded-dmg-check.XXXXXX)"
ATTACHED=0

cleanup() {
    if [ "$ATTACHED" = 1 ]; then
        hdiutil detach "$MOUNT_DIR" >/dev/null 2>&1 || true
    fi
    rm -rf "$TEST_ROOT" "$MOUNT_DIR"
}
trap cleanup EXIT

mkdir -p "$SOURCE_APP/Contents"
echo 'real-application-payload' > "$SOURCE_APP/Contents/real-marker.txt"

bash "$CREATOR" "$SOURCE_APP" "$OUTPUT_DMG" 'MarkLeaf Layout Integration Test'

hdiutil attach -nobrowse -readonly -mountpoint "$MOUNT_DIR" "$OUTPUT_DMG" >/dev/null
ATTACHED=1

[ -f "$MOUNT_DIR/MarkLeaf.app/Contents/real-marker.txt" ] || \
    fail 'the placeholder app was not replaced by the real application'
[ -L "$MOUNT_DIR/Applications" ] || fail 'Applications shortcut is missing'
[ "$(readlink "$MOUNT_DIR/Applications")" = '/Applications' ] || \
    fail 'Applications shortcut points to the wrong destination'
[ -f "$MOUNT_DIR/.background/MarkLeaf-dmg-background.png" ] || \
    fail 'branded background resource is missing'
[ -f "$MOUNT_DIR/.DS_Store" ] || fail 'Finder layout metadata is missing'
strings "$MOUNT_DIR/.DS_Store" | grep -Fq 'MarkLeaf-dmg-background.png' || \
    fail 'Finder layout metadata does not reference the branded background'

DISK_NAME="$(basename "$MOUNT_DIR")"
LAYOUT_SCRIPT="$TEST_ROOT/read-layout.applescript"
cat > "$LAYOUT_SCRIPT" <<'APPLESCRIPT'
on run argv
    set mountedDiskName to item 1 of argv
    set outputItems to {}
    tell application "Finder"
        set targetDisk to disk mountedDiskName
        open targetDisk
        delay 3
        set targetWindow to container window of targetDisk
        set viewOptions to icon view options of targetWindow
        set end of my outputItems to "bounds=" & (bounds of targetWindow as text)
        set end of my outputItems to "iconSize=" & (icon size of viewOptions as text)
        set end of my outputItems to "app=" & (position of item "MarkLeaf.app" of targetDisk as text)
        set end of my outputItems to "applications=" & (position of item "Applications" of targetDisk as text)
        close targetWindow
    end tell
    set AppleScript's text item delimiters to linefeed
    return outputItems as text
end run
APPLESCRIPT
set +e
LAYOUT="$(/usr/bin/osascript "$LAYOUT_SCRIPT" "$DISK_NAME" 2>&1)"
LAYOUT_STATUS=$?
set -e
[ "$LAYOUT_STATUS" -eq 0 ] || fail "Finder could not read the final layout: $LAYOUT"

for expected in \
    'bounds=100100740500' \
    'iconSize=112' \
    'app=165220' \
    'applications=475220'; do
    printf '%s\n' "$LAYOUT" | grep -Fqx "$expected" || \
        fail "final Finder layout missing: $expected; got: $LAYOUT"
done

echo 'PASS: branded MarkLeaf DMG renders the expected Finder layout'
