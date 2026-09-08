#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-font-window-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

cp "$ROOT_DIR/script/tests/OptionalFontsWindowLayoutTest.swift" "$BUILD_DIR/main.swift"

DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode-beta.app/Contents/Developer}" \
CLANG_MODULE_CACHE_PATH="$BUILD_DIR/module-cache" \
SWIFT_MODULECACHE_PATH="$BUILD_DIR/module-cache" \
xcrun swiftc \
  "$ROOT_DIR/Sources/MarkLeaf/Services/OptionalFontCatalog.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/OptionalFontInstaller.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Views/OptionalFontsWindowController.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/test"

"$BUILD_DIR/test"
