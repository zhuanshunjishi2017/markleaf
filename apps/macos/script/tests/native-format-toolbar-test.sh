#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-format-toolbar-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/NativeFormatToolbarTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$(xcrun --sdk macosx --show-sdk-path)" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Models/EditorActions.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Views/NativeFormatToolbarView.swift" \
  "$BUILD_DIR/main.swift" -o "$BUILD_DIR/toolbar-test"
"$BUILD_DIR/toolbar-test"
