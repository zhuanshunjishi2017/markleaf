#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$1" "$MENU" || fail "missing: $1"; }
reject() { if grep -Fq "$1" "$MENU"; then fail "unexpected: $1"; fi; }

require 'menu.removeAllItems()'
reject 'for item in menu.items where item.tag >= 100'
echo PASS
