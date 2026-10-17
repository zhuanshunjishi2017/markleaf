#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
SHORTCUTS="$ROOT_DIR/Sources/MarkLeaf/Support/ShortcutSettings.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

grep -Fq 'L10n.t("段落专注模式")' "$MENU" \
  || fail "paragraph focus must use the shared label"
grep -Fq 'titleKey: "段落专注模式"' "$SHORTCUTS" \
  || fail "paragraph focus shortcut must use the shared label"

echo PASS
