#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-window-close-policy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/WindowClosePolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WindowClosePolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/window-close-policy-test"
"$BUILD_DIR/window-close-policy-test"

WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
grep -Fq 'WindowClosePolicy.shouldCloseAllTabs' "$WINDOW" || {
  echo "FAIL: window close delegate must route to the all-tabs policy" >&2
  exit 1
}
grep -Fq 'self.closeTabs(ids)' "$WINDOW" || {
  echo "FAIL: window close delegate must close every tab ID" >&2
  exit 1
}
if grep -Fq 'TabShortcutPolicy.closesWindow' "$WINDOW"; then
  echo "FAIL: closing a tab must not close the native window" >&2
  exit 1
fi

echo "PASS"
