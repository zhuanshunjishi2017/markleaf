#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WINDOW_CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
EDITOR_SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
grep -Fq 'TabSwitchSavePolicy.action' "$WINDOW_CONTROLLER"
grep -Fq 'flushRecoverySnapshotNow' "$WINDOW_CONTROLLER"
grep -Fq 'func flushRecoverySnapshotNow' "$EDITOR_SESSION"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-tab-switch-save-policy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/TabSwitchSavePolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" "$ROOT_DIR/Sources/MarkLeaf/Services/TabSwitchSavePolicy.swift" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/tab-switch-save-policy-test"
"$BUILD_DIR/tab-switch-save-policy-test"
