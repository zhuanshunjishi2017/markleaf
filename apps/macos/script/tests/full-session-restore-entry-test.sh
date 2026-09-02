#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
DELEGATE="$ROOT_DIR/Sources/MarkLeaf/App/AppDelegate.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
grep -Fq 'func restoreFullSession(explicitFile: String?)' "$MANAGER"
grep -Fq 'SessionStore.shared.loadLatest()' "$MANAGER"
grep -Fq 'restoreFullSession(explicitFile: explicitFile)' "$DELEGATE"
grep -Fq 'restoreInitialTabIfNeeded' "$WINDOW"
echo PASS
