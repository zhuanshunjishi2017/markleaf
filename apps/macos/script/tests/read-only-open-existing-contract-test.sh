#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

readonly_existing="$(sed -n '/if readOnly, let target = windowSession.session(for: id)/,/let window {/p' "$WINDOW")"
printf '%s' "$readonly_existing" | grep -Fq '!target.isReadOnly,' \
  || fail "a permission-read-only existing document must not prompt again"
printf '%s' "$readonly_existing" | grep -Fq '!target.isReadOnlyLocked' \
  || fail "explicitly locked documents must not prompt again"

echo PASS
