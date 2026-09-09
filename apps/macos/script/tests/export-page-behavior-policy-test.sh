#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-export-page-behavior-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
  cp "$ROOT_DIR/script/tests/ExportPageBehaviorPolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Support/AppLog.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/PDFHeaderFooterPolicy.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ThemeIDNormalizer.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/PersistedExportSettings.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/PDFOutlineBuilder.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/PDFGenerator.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/export-page-behavior-policy-test"
"$BUILD_DIR/export-page-behavior-policy-test"
