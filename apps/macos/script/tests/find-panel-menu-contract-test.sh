#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU_FILE="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"

grep -q 'commandItem(L10n.t("查找与替换"), "find", key: "f")' "$MENU_FILE"
if grep -q 'commandItem(L10n.t("替换"), "replace"' "$MENU_FILE"; then
  echo "FAIL: Edit menu still exposes a separate Replace entry" >&2
  exit 1
fi

echo "PASS"
