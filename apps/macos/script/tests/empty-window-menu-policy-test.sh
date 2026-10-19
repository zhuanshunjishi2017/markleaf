#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/MenuCommandAvailabilityPolicy.swift"
[[ -f "$POLICY" ]] || { echo "FAIL: missing MenuCommandAvailabilityPolicy.swift" >&2; exit 1; }

BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-empty-menu-policy.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/EmptyWindowMenuPolicyTest.swift" "$BUILD_DIR/main.swift"
xcrun swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$POLICY" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
