#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-export-images-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

cp "$ROOT_DIR/script/tests/ExportLocalImageEmbedderTest.swift" "$BUILD_DIR/main.swift"
xcrun swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ExportLocalImageEmbedder.swift" \
  "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test" "$BUILD_DIR/fixtures"
