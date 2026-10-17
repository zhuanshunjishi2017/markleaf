#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
SIDEBAR="$ROOT_DIR/Sources/MarkLeaf/Views/SidebarView.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

workspace_block="$(sed -n '/func showWorkspaceTab()/,/^[[:space:]]*}$/p' "$SESSION")"
outline_block="$(sed -n '/func showOutlineTab()/,/func toggleDetachedOutline/p' "$SESSION")"

grep -Fq 'requestSidebarFocus(.workspace)' <<<"$workspace_block" \
  || fail "workspace navigation must focus the revealed workspace view"
grep -Fq 'requestSidebarFocus(.outline)' <<<"$outline_block" \
  || fail "merged outline navigation must focus the revealed outline view"
grep -Fq 'requestSidebarFocus(.detachedOutline)' <<<"$outline_block" \
  || fail "detached outline navigation must defer focus until the restored layout is applied"

grep -Fq 'func focusWorkspace()' "$SIDEBAR" \
  || fail "sidebar must expose workspace focus"
grep -Fq 'func focusMergedOutline()' "$SIDEBAR" \
  || fail "sidebar must expose merged outline focus"
grep -Fq 'PendingSidebarFocus' "$CONTROLLER" \
  || fail "window controller must defer focus until the view state is applied"

echo PASS
