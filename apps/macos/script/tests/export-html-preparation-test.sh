#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-export-routing-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

cp "$ROOT_DIR/script/tests/ExportHTMLPreparationTest.swift" "$BUILD_DIR/main.swift"
xcrun swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ExportLocalImageEmbedder.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ExportHTMLPreparation.swift" \
  "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
