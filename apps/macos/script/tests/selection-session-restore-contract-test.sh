#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
MANIFEST="$ROOT_DIR/Sources/MarkLeaf/Services/SessionManifest.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

require_text "$SESSION" 'private var pendingInitialSelection: PendingDocumentSelection?'
require_text "$SESSION" 'func openInitialDocument(prepared: PreparedDocument, selection: PendingDocumentSelection? = nil)'
require_text "$SESSION" 'loadPreparedDocument(prepared, selection: pendingInitialSelection)'
require_text "$SESSION" 'visualSelectionFrom: selection?.visualFrom'
require_text "$CONTROLLER" 'visualFrom: tab.visualSelectionFrom'
require_text "$CONTROLLER" 'sourceTo: tab.sourceSelectionTo'
require_text "$MANIFEST" 'var visualSelectionFrom: Int? = nil'
require_text "$MANIFEST" 'var sourceSelectionTo: Int? = nil'

echo PASS
