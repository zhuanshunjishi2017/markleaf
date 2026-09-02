#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD_DIR="$(mktemp -d /tmp/markleaf-editor-host-transition-policy-test.XXXXXX)"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="$(xcrun --sdk macosx --show-sdk-path)"
cp "$ROOT_DIR/script/tests/EditorHostTransitionPolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/EditorHostTransitionPolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/editor-host-transition-policy-test"
"$BUILD_DIR/editor-host-transition-policy-test"
