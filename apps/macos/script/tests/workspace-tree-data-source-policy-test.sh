#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-workspace-tree-policy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/WorkspaceTreeDataSourcePolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/WorkspaceTreeDataSourcePolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/workspace-tree-data-source-policy-test"
"$BUILD_DIR/workspace-tree-data-source-policy-test"
