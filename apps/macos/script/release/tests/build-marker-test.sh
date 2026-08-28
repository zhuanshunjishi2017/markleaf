#!/usr/bin/env bash

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WRITER="$HERE/../write-build-marker.sh"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-build-marker-test.XXXXXX")"
MARKER="$TEST_ROOT/MarkLeaf-build-328.txt"

cleanup() {
    rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

bash "$WRITER" "$MARKER" "1.4.0" "328" "6d5c93f"

expected=$'version=1.4.0\nbuild=328\ncommit=6d5c93f\n'
actual="$(cat "$MARKER")"$'\n'
if [[ "$actual" != "$expected" ]]; then
    echo "FAIL: build marker contents differ" >&2
    exit 1
fi

if bash "$WRITER" "$MARKER" "1.4.0" "invalid" "6d5c93f" >/dev/null 2>&1; then
    echo "FAIL: non-numeric build number was accepted" >&2
    exit 1
fi

echo "PASS: build marker has the canonical version, build, and commit fields"
