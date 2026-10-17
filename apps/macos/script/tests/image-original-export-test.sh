#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-image-original-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
swiftc -parse-as-library -module-cache-path "$BUILD_DIR/module-cache" "$ROOT_DIR/Sources/MarkLeaf/Services/ImageOriginalExportService.swift" "$ROOT_DIR/script/tests/ImageOriginalExportServiceTest.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
