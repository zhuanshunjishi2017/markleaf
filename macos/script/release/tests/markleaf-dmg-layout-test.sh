#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="$(cd "$HERE/.." && pwd)"
HELPER="$RELEASE_DIR/markleaf-dmg-layout.sh"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

[ -x "$HELPER" ] || fail "Missing executable helper: $HELPER"

TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-dmg-layout.XXXXXX")"
trap 'rm -rf "$TEST_ROOT"' EXIT

STAGE="$TEST_ROOT/stage"
mkdir -p "$STAGE/MarkLeaf.app"
ln -s /Applications "$STAGE/Applications"

bash "$HELPER" prepare "$STAGE"

BACKGROUND="$STAGE/.background/MarkLeaf-dmg-background.png"
[ -f "$BACKGROUND" ] || fail 'prepare did not stage the DMG background'
[ -L "$STAGE/Applications" ] || fail 'prepare replaced the Applications symlink'
[ "$(readlink "$STAGE/Applications")" = '/Applications' ] || fail 'Applications symlink target changed'

CAPTURE="$TEST_ROOT/finder.applescript"
FAKE_OSASCRIPT="$TEST_ROOT/osascript"
cat > "$FAKE_OSASCRIPT" <<'FAKE'
#!/usr/bin/env bash
cat > "$TEST_CAPTURE"
exit 23
FAKE
chmod +x "$FAKE_OSASCRIPT"

TEST_CAPTURE="$CAPTURE" OSASCRIPT_BIN="$FAKE_OSASCRIPT" \
    bash "$HELPER" apply "$STAGE"

for expected in \
    'set mountedPath to item 2 of argv' \
    'set targetDisk to disk (POSIX file mountedPath as alias)' \
    'set bounds of targetWindow to {100, 100, 740, 500}' \
    'set icon size of viewOptions to 112' \
    'set text size of viewOptions to 13' \
    'set background picture of viewOptions to file ".background:MarkLeaf-dmg-background.png" of targetDisk' \
    'set position of item "MarkLeaf.app" of targetDisk to {165, 220}' \
    'set position of item "Applications" of targetDisk to {475, 220}'; do
    grep -Fq "$expected" "$CAPTURE" || fail "Finder layout missing: $expected"
done

[ -d "$STAGE/MarkLeaf.app" ] || fail 'apply removed MarkLeaf.app after Finder failure'
[ -L "$STAGE/Applications" ] || fail 'apply removed Applications after Finder failure'

echo 'PASS: MarkLeaf DMG layout helper'
