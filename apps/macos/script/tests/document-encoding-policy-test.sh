#!/bin/bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-encoding-policy-test.XXXXXX")"
trap 'rm -rf "$TMP_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/DocumentEncodingPolicyTest.swift" "$TMP_DIR/main.swift"

swiftc -sdk "$SDK_PATH" -module-cache-path "$TMP_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/DocumentEncodingPolicy.swift" \
  "$TMP_DIR/main.swift" \
  -o "$TMP_DIR/document-encoding-policy-test"
"$TMP_DIR/document-encoding-policy-test"
