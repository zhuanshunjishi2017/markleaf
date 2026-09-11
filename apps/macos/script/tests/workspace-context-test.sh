#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "$ROOT_DIR/script/tests/document-core-harness.sh"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-workspace-context-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
export MARKLEAF_APP_SUPPORT_DIR="$BUILD_DIR/app-support"
cp "$ROOT_DIR/script/tests/WorkspaceContextTest.swift" "$BUILD_DIR/main.swift"
compile_with_document_core -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceSortOrder.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Models/WorkspaceModel.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/DocumentEncodingPolicy.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspacePreviewCache.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceDocumentPolicy.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceScanner.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceWatcher.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Support/AppLog.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceContext.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/workspace-context-test"
"$BUILD_DIR/workspace-context-test"
