#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-text-parity.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/WorkspaceTextParityTest.swift" "$BUILD_DIR/main.swift"
swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Models/WorkspaceModel.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/DocumentEncodingPolicy.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/HTMLEntities.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/MarkdownPlainText.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspacePreviewCache.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceDocumentPolicy.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceScanner.swift" \
  "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test" "$ROOT_DIR/../../tests/fixtures/workspace-text.json"
