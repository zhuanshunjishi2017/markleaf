#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-encoding-change-policy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/DocumentEncodingChangePolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/DocumentEncodingPolicy.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/DocumentEncodingChangePolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/encoding-change-policy-test"
"$BUILD_DIR/encoding-change-policy-test"
