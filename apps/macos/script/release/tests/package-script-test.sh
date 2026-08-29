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
    'Contents/Resources/Welcome' \
    'DMG_VOLUME_NAME=' \
    'swift build' \
    'dsymutil' \
    'create-branded-dmg.sh' \
    'codesign --verify' \
    'shasum -a 256'; do
    grep -Fq "$expected" "$PACKAGE" || fail "Package script is missing: $expected"
done

if grep -Fq 'pkill' "$PACKAGE"; then
    fail 'Package script must not stop a running MarkLeaf process'
fi

echo 'PASS: MarkLeaf package script contract'
