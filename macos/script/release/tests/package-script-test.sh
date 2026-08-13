#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="$(cd "$HERE/.." && pwd)"
PACKAGE="$RELEASE_DIR/package.sh"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

[ -x "$PACKAGE" ] || fail "Missing executable package script: $PACKAGE"

for expected in \
    'prepare_resources.sh' \
    'DMG_VOLUME_NAME=' \
    'mktemp -d /private/tmp/markleaf-dmg-stage' \
    'swift build' \
    'dsymutil' \
    'hdiutil create' \
    'hdiutil attach' \
    'sleep 5' \
    'hdiutil convert' \
    'markleaf-dmg-layout.sh' \
    'mktemp -d /private/tmp/markleaf-dmg-mount' \
    'codesign --verify' \
    'shasum -a 256'; do
    grep -Fq "$expected" "$PACKAGE" || fail "Package script is missing: $expected"
done

if grep -Fq 'pkill' "$PACKAGE"; then
    fail 'Package script must not stop a running MarkLeaf process'
fi

echo 'PASS: MarkLeaf package script contract'
