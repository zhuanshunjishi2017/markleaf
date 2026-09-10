#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-windows-sync-parity.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"

cp "$ROOT_DIR/script/tests/WindowsSyncParityContractTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Support/ShortcutSettings.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/windows-sync-parity-test"

"$BUILD_DIR/windows-sync-parity-test" \
  "$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/EditorCommandRouter.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"
