#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-changelog-open-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/ChangelogOpenPolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ChangelogOpenPolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/test"
"$BUILD_DIR/test"

APP_WINDOW_MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
grep -q 'ChangelogOpenPolicy.destination' "$APP_WINDOW_MANAGER" || {
  echo "FAIL: openChangelog must route through ChangelogOpenPolicy" >&2
  exit 1
}
grep -q 'openFileInTab(target, readOnly: true)' "$APP_WINDOW_MANAGER" || {
  echo "FAIL: multi-tab changelog opening must reuse the active-window file-tab path" >&2
  exit 1
}
