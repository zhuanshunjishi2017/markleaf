#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PANEL="$ROOT_DIR/Sources/MarkLeaf/Views/FindPanelController.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$2" "$1" || fail "$1 missing: $2"; }

require "$PANEL" 'func detachIfBound(to sessions: [EditorSession?]) -> Bool'
require "$MANAGER" 'func closeFindPanelIfBound(to sessions: [EditorSession?])'
require "$WINDOW" 'closeFindPanelIfBound(to: [closingSession])'
require "$WINDOW" 'closeFindPanelIfBound(to: closedSessions)'
echo PASS
