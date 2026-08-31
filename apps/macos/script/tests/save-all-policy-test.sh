#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-save-all-policy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/SaveAllPolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" "$ROOT_DIR/Sources/MarkLeaf/Services/FileIdentity.swift" "$ROOT_DIR/Sources/MarkLeaf/Services/TabStore.swift" "$ROOT_DIR/Sources/MarkLeaf/Services/SaveAllPolicy.swift" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test"

# Integration contract: the command is exposed through the native menu and routed
# to the window-level saver, while untitled saves hand their acquired identity back
# to the tab model.
rg -q 'commandItem\(L10n\.t\("保存全部"\), "saveAll"' "$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
rg -q 'case "saveAll": saveAllRequest\?\(\)' "$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
rg -q 'session\.onAcquiredFileURL' "$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
printf 'PASS integration contracts\n'
