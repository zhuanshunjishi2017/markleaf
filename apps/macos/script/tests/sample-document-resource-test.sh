#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-sample-resource-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
APP_MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
grep -Fq 'func openSample(_ sample: SampleDocumentResource)' "$APP_MANAGER" || {
  echo "FAIL: missing sample document opener" >&2
  exit 1
}
grep -Fq 'PreparedDocument(url: target, markdown: markdown, isReadOnly: true)' "$APP_MANAGER" || {
  echo "FAIL: sample documents must open read-only" >&2
  exit 1
}
cp "$ROOT_DIR/script/tests/SampleDocumentResourceTest.swift" "$BUILD_DIR/main.swift"
swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/SampleDocumentResource.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/sample-document-resource-test"
"$BUILD_DIR/sample-document-resource-test"
