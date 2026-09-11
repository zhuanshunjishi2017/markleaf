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
    'Contents/Resources/Samples' \
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

python3 - "$PACKAGE" <<'PY'
import pathlib, subprocess, sys
source = pathlib.Path(sys.argv[1]).read_text()
commands = [line for line in source.splitlines()
            if line.startswith('swift build ') or line.startswith('BUILD_BIN=')]
assert len(commands) == 2, 'Expected both release build and binary-path queries'
for has_flag in [False, True]:
    setup = r'''
set -euo pipefail
MACOS_DIR='/tmp/markleaf test package'
APP_NAME=MarkLeaf
SWIFT_BUILD_FLAGS=()
swift() {
    [[ "$1" == build ]] || exit 90
    shift
    if [[ "$EXPECT_FLAG" == 1 ]]; then
        [[ "$1" == --disable-sandbox ]] || exit 91
        shift
    fi
    [[ "$1" == --package-path && "$2" == "$MACOS_DIR" ]] || exit 92
    printf '%s\n' '/tmp/markleaf-build'
}
'''
    setup += f'EXPECT_FLAG={int(has_flag)}\n'
    if has_flag:
        setup += 'SWIFT_BUILD_FLAGS+=(--disable-sandbox)\n'
    subprocess.run(['/bin/bash', '-c', setup + '\n'.join(commands)], check=True, stdout=subprocess.PIPE)
PY

echo 'PASS: MarkLeaf package script contract'
