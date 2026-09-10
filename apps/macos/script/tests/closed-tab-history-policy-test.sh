#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOURCE="$ROOT_DIR/Sources/MarkLeaf/Services/ClosedTabHistory.swift"
if [[ ! -f "$SOURCE" ]]; then
  echo "FAIL: missing ClosedTabHistory.swift" >&2
  exit 1
fi
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-closed-tab-history.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/ClosedTabHistoryPolicyTest.swift" "$BUILD_DIR/main.swift"
xcrun swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/NewDocumentKind.swift" \
  "$SOURCE" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
