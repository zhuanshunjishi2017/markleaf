#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-optional-font-installer-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode-beta.app/Contents/Developer}"
export DEVELOPER_DIR
export SWIFT_MODULECACHE_PATH="$BUILD_DIR/module-cache"
export CLANG_MODULE_CACHE_PATH="$BUILD_DIR/module-cache"
cp "$ROOT_DIR/script/tests/OptionalFontInstallerCoreTest.swift" "$BUILD_DIR/main.swift"

xcrun swiftc \
  "$ROOT_DIR/Sources/MarkLeaf/Services/OptionalFontCatalog.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/OptionalFontInstaller.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/optional-font-installer-test"

"$BUILD_DIR/optional-font-installer-test"
