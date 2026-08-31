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
grep -Fq 'WindowClosePolicy.closesWindowOnTrafficLight' "$WINDOW" || {
  echo "FAIL: window close delegate must route to the traffic-light policy" >&2
  exit 1
}
grep -Fq 'SequentialDocumentDispositionQueue.run' "$WINDOW" || {
  echo "FAIL: window close must still run the save-prompt queue" >&2
  exit 1
}
if ! sed -n '/func windowShouldClose/,/private func closeWindowForReal/p' "$WINDOW" \
     | grep -Fq 'closeWindowForReal()'; then
  echo "FAIL: a passed save-queue must close the window itself" >&2
  exit 1
fi
if sed -n '/func windowShouldClose/,/private func closeWindowForReal/p' "$WINDOW" \
   | grep -Fq 'closeTabs(ids)'; then
  echo 'FAIL: the red traffic-light button must not merely close tabs' >&2
  exit 1
fi

echo "PASS"
