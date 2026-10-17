#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PANEL="$ROOT_DIR/Sources/MarkLeaf/Views/FindPanelController.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$2" "$1" || fail "$1 missing: $2"; }

require "$PANEL" 'func detachIfBound(to sessions: [EditorSession?]) -> Bool'
require "$PANEL" 'GlassWindowAttachment.attach(contentView, to: window, style: .interactive)'
require "$PANEL" 'window.styleMask.insert(.fullSizeContentView)'
require "$PANEL" 'window.backgroundColor = .clear'
require "$ROOT_DIR/Sources/MarkLeaf/Views/GlassSurfaceView.swift" 'NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency'
if [ "$(grep -Fc 'GlassWindowAttachment.attach' "$PANEL")" -ne 1 ]; then
  fail "find panel must have exactly one glass attachment"
fi
require "$MANAGER" 'func closeFindPanelIfBound(to sessions: [EditorSession?])'
require "$WINDOW" 'closeFindPanelIfBound(to: [closingSession])'
require "$WINDOW" 'closeTab(id, reason: .closeTab, completion: done)'
echo PASS
