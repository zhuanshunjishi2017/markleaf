#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-workspace-deletion-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

cp "$ROOT_DIR/script/tests/WorkspaceDeletionPolicyTest.swift" "$BUILD_DIR/main.swift"
SDK_PATH="$(xcrun --sdk macosx --show-sdk-path)"
xcrun swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/FileIdentity.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/SessionManifest.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/TabStore.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceDeletionPolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
