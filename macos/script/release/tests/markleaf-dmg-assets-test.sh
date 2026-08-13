#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="$(cd "$HERE/.." && pwd)"
SVG="$RELEASE_DIR/dmg-assets/MarkLeaf-dmg-background.svg"
PNG="$RELEASE_DIR/dmg-assets/MarkLeaf-dmg-background.png"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

[ -f "$SVG" ] || fail "Missing $SVG"
[ -f "$PNG" ] || fail "Missing $PNG"
grep -Fq 'MarkLeaf' "$SVG" || fail 'SVG is missing MarkLeaf brand text'
grep -Fq 'MARKDOWN WITHOUT DISTRACTION' "$SVG" || fail 'SVG is missing subtitle'
grep -Fq 'drag to' "$SVG" || fail 'SVG is missing drag instruction'

WIDTH="$(sips -g pixelWidth "$PNG" | awk '/pixelWidth/ { print $2 }')"
HEIGHT="$(sips -g pixelHeight "$PNG" | awk '/pixelHeight/ { print $2 }')"
[ "$WIDTH" = '1280' ] || fail "Expected 1280px PNG width, got $WIDTH"
[ "$HEIGHT" = '800' ] || fail "Expected 800px PNG height, got $HEIGHT"

echo 'PASS: MarkLeaf DMG assets'
