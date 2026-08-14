#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="$(cd "$HERE/.." && pwd)"
SVG="$RELEASE_DIR/dmg-assets/MarkLeaf-dmg-background.svg"
PNG="$RELEASE_DIR/dmg-assets/MarkLeaf-dmg-background.png"
APP_ICON="$RELEASE_DIR/dmg-assets/MarkLeaf-app-icon.png"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

[ -f "$SVG" ] || fail "Missing $SVG"
[ -f "$PNG" ] || fail "Missing $PNG"
[ -f "$APP_ICON" ] || fail "Missing $APP_ICON"
grep -Fq 'MarkLeaf' "$SVG" || fail 'SVG is missing MarkLeaf brand text'
grep -Fq 'MARKDOWN WITHOUT DISTRACTION' "$SVG" || fail 'SVG is missing subtitle'
grep -Fq 'drag to' "$SVG" || fail 'SVG is missing drag instruction'
grep -Fq 'MarkLeaf-app-icon.png' "$SVG" || fail 'SVG is not using the application icon asset'
grep -Fq 'preserveAspectRatio="xMidYMid meet"' "$SVG" || fail 'SVG is missing aspect-ratio preservation'

WIDTH="$(sips -g pixelWidth "$PNG" | awk '/pixelWidth/ { print $2 }')"
HEIGHT="$(sips -g pixelHeight "$PNG" | awk '/pixelHeight/ { print $2 }')"
[ "$WIDTH" = '1280' ] || fail "Expected 1280px PNG width, got $WIDTH"
[ "$HEIGHT" = '800' ] || fail "Expected 800px PNG height, got $HEIGHT"

echo 'PASS: MarkLeaf DMG assets'
