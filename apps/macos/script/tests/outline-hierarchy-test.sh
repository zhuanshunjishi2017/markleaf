#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-outline-hierarchy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode-beta.app/Contents/Developer}"
SDK_PATH="$(DEVELOPER_DIR="$DEVELOPER_DIR" xcrun --sdk macosx --show-sdk-path)"
cp "$ROOT_DIR/script/tests/OutlineHierarchyTest.swift" "$BUILD_DIR/main.swift"
DEVELOPER_DIR="$DEVELOPER_DIR" xcrun swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Models/WorkspaceModel.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/OutlineHierarchy.swift" \
  "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
