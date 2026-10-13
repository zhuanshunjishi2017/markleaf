#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CONTEXT="$ROOT_DIR/macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"
TOOLBAR="$ROOT_DIR/macos/Sources/MarkLeaf/Views/NativeFormatToolbarView.swift"
SESSION="$ROOT_DIR/macos/Sources/MarkLeaf/Services/EditorSession.swift"

require() {
  grep -Fq "$2" "$1" || { echo "FAIL: missing $2 in $1" >&2; exit 1; }
}
reject() {
  if grep -Fq "$2" "$1"; then echo "FAIL: obsolete $2 in $1" >&2; exit 1; fi
}

# One NSMenu owns both the quick row and ordinary context commands.
require "$CONTEXT" 'item.view = toolbar'
require "$CONTEXT" 'NativeFormatToolbarView('
require "$CONTEXT" 'NSMenuDelegate'
require "$CONTEXT" 'func menuDidClose('
require "$CONTEXT" 'execute("endContextMenu")'
for obsolete in 'toolbarFrame' 'EditorContextMenuSession' 'addLocalMonitorForEvents' 'menuHeight'; do
  reject "$CONTEXT" "$obsolete"
done
require "$TOOLBAR" 'NSButton'
require "$TOOLBAR" 'mouseEntered'
require "$TOOLBAR" 'mouseExited'
require "$TOOLBAR" '.activeAlways'
require "$TOOLBAR" 'button.focusRingType = .none'
require "$TOOLBAR" 'button.refusesFirstResponder = true'
require "$SESSION" 'RunLoop.main.perform(inModes: [.default])'
require "$CONTEXT" 'if let item, !(item.view is NativeFormatToolbarView)'
echo 'PASS: native format row shares the context menu lifecycle'
