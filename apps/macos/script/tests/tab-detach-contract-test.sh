#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TAB="$ROOT_DIR/Sources/MarkLeaf/Views/TabBarController.swift"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

require_text "$TAB" 'var onDetach: ((DocumentTabID) -> Void)?'
require_text "$TAB" 'TabDetachPolicy.action(globalPoint: globalRect.origin, windowFrame: window?.frame ?? .zero) == .detach'
require_text "$CONTROLLER" 'func detachTabToNewWindow(_ id: DocumentTabID)'
require_text "$CONTROLLER" 'DetachedTabDocument('
require_text "$CONTROLLER" 'removeDetachedTab(id)'
require_text "$SESSION" 'func openInitialDocument('
require_text "$SESSION" 'pendingInitialDetachedDocument'
require_text "$MANAGER" 'func newWindow(detachedDocument: DetachedTabDocument)'
echo PASS
