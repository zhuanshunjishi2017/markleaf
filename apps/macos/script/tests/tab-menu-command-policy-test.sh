#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/MenuCommandAvailabilityPolicy.swift"
if [[ ! -f "$POLICY" ]]; then
  echo "FAIL: missing MenuCommandAvailabilityPolicy.swift" >&2
  exit 1
fi
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-tab-menu-policy.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
cp "$ROOT_DIR/script/tests/TabMenuCommandPolicyTest.swift" "$BUILD_DIR/main.swift"
xcrun swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$POLICY" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"
