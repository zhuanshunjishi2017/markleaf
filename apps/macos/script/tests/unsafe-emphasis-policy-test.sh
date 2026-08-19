#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-unsafe-emphasis-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/UnsafeEmphasisPolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc \
  -sdk "$SDK_PATH" \
  -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/UnsafeEmphasisPolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/policy-test"

"$BUILD_DIR/policy-test"
