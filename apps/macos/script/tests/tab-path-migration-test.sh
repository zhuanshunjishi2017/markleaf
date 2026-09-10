#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-tab-path-migration-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/TabPathMigrationTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" "$ROOT_DIR/Sources/MarkLeaf/Services/FileIdentity.swift" "$ROOT_DIR/Sources/MarkLeaf/Services/TabStore.swift" "$ROOT_DIR/Sources/MarkLeaf/Services/TabPathMigration.swift" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
