#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD_DIR="$(mktemp -d /tmp/markleaf-sidebar-state-source-policy-test.XXXXXX)"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="$(xcrun --sdk macosx --show-sdk-path)"
cp "$ROOT_DIR/script/tests/SidebarStateSourcePolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/SidebarStateSourcePolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/sidebar-state-source-policy-test"
"$BUILD_DIR/sidebar-state-source-policy-test"
