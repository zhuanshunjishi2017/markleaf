#!/usr/bin/env bash
set -euo pipefail

MACOS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ROOT_DIR="$(cd "$MACOS_DIR/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-theme-migration-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$MACOS_DIR/script/tests/StyleManagerThemeMigrationTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$MACOS_DIR/Sources/MarkLeaf/Services/ThemeIDNormalizer.swift" \
  "$MACOS_DIR/Sources/MarkLeaf/Services/StyleManager.swift" \
  "$MACOS_DIR/Sources/MarkLeaf/Services/L10n.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/style-manager-theme-migration-test"
"$BUILD_DIR/style-manager-theme-migration-test" "$ROOT_DIR"
