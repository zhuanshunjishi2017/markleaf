#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "$ROOT_DIR/script/tests/document-core-harness.sh"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-session-store-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/SessionStoreTest.swift" "$BUILD_DIR/main.swift"
compile_with_document_core -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" "$ROOT_DIR/Sources/MarkLeaf/Services/SessionManifest.swift" "$ROOT_DIR/Sources/MarkLeaf/Services/SessionStore.swift" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
