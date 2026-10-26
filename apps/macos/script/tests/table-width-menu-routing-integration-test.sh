#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-table-menu-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/TableWidthMenuRoutingIntegrationTest.swift" "$BUILD_DIR/main.swift"
sources=()
while IFS= read -r source; do
  [[ "$source" == "$ROOT_DIR/Sources/MarkLeaf/main.swift" ]] || sources+=("$source")
done < <(rg --files "$ROOT_DIR/Sources/MarkLeaf" -g '*.swift')
swiftc -sdk "$(xcrun --sdk macosx --show-sdk-path)" -module-cache-path "$BUILD_DIR/module-cache" \
  "${sources[@]}" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/table-menu-test"
MARKLEAF_APP_SUPPORT_DIR="$BUILD_DIR/settings" "$BUILD_DIR/table-menu-test"
