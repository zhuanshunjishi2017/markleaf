#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="$(cd "$HERE/.." && pwd)"
WRITER="$RELEASE_DIR/write-info-plist.sh"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-package-metadata-test.XXXXXX")"
INFO_PLIST="$TEST_ROOT/Info.plist"

cleanup() {
    rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

MARKLEAF_BUILD=987 "$WRITER" "$INFO_PLIST" "1.3.2"

expect_plist_value() {
    local key="$1"
    local expected="$2"
    local actual
    actual="$(plutil -extract "$key" raw "$INFO_PLIST")"
    if [[ "$actual" != "$expected" ]]; then
        echo "FAIL: expected $key=$expected, got $actual" >&2
        exit 1
    fi
}

expect_plist_value CFBundleShortVersionString "1.3.2"
expect_plist_value CFBundleVersion "987"
expect_plist_value NSHumanReadableCopyright "Copyright © 2026 zhuanshunjishi2017 & NaBian"

echo "PASS: release package metadata preserves build number and copyright"
