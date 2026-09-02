#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
grep -Fq 'makeMemoryPressureSource' "$MANAGER"
grep -Fq 'suspendBackgroundTabsUnderPressure' "$MANAGER"
grep -Fq 'suspendBackgroundTabsIfNeeded' "$WINDOW"
grep -Fq 'TabMemoryPressurePolicy.suspensionOrder' "$WINDOW"
echo PASS
