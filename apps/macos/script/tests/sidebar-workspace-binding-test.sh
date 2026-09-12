#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
SIDEBAR="$ROOT_DIR/Sources/MarkLeaf/Views/SidebarView.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"

fail() {
    echo "FAIL: $1" >&2
    exit 1
}

# The initial editor tab and WindowSession must share one WorkspaceContext.
new_window_body="$(sed -n '/func newWindow(documentPath:/,/func newWindow(preparedDocument:/p' "$APP_MANAGER")"
printf '%s' "$new_window_body" | grep -Fq 'let session = EditorSession()' \
    && fail "initial editor still creates a separate workspace before WindowSession"
printf '%s' "$new_window_body" | grep -Fq 'let session = EditorSession(workspace: windowSession.workspace)' \
    || fail "initial editor must use WindowSession workspace"

# Rebinding the sidebar must also rebind and refresh its workspace tree.
rebind_body="$(sed -n '/func rebind(to session: EditorSession)/,/^    }/p' "$SIDEBAR")"
printf '%s' "$rebind_body" | grep -Fq 'workspaceTree.rebind(to: session, preservingTopology: topologyUnchanged)' \
    || fail "sidebar rebind must update WorkspaceTreeView session"
printf '%s' "$rebind_body" | grep -Fq 'workspaceChanged()' \
    || fail "sidebar rebind must refresh workspace state"
printf '%s' "$rebind_body" | grep -Fq 'outlineChanged()' \
    || fail "sidebar rebind must refresh the outline"
printf '%s' "$rebind_body" | grep -Fq 'selectTab(session.sidebarTabIndex, persist: false)' \
    || fail "sidebar rebind must reapply outline exclusion to segmented control"

# Right-outline state is window-level.  Rebinding an active tab must copy it into
# that tab's session so the sidebar cannot route around the menu's exclusion.
rebind_active_body="$(sed -n '/private func rebindActiveSessionUI/,/private func configureTabSession/p' "$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift")"
printf '%s' "$rebind_active_body" | grep -Fq 'session.outlineDetached = self.session.outlineDetached' \
    || fail "active tab rebind must inherit window-level right-outline state"
printf '%s' "$rebind_active_body" | grep -Fq 'if session.outlineDetached, session.sidebarTabIndex == 1' \
    || fail "active tab rebind must keep right-outline tabs mutually exclusive"

# Outline rows keep editor positions for a specific session.  Rebinding must also
# rebuild the click callback, otherwise clicks scroll the first tab's old editor.
outline_rebind_body="$(sed -n '/final class OutlineTreeView/,/func synchronizeSelection/p' "$SIDEBAR")"
printf '%s' "$outline_rebind_body" | grep -Fq 'onHeadingActivated = { [weak session] heading in' \
    || fail "outline tree rebind must rebuild the heading activation callback"
printf '%s' "$outline_rebind_body" | grep -Fq 'session?.scrollToPosition(heading.position)' \
    || fail "outline tree rebind must target the newly bound session"

# A workspace file must not be written until its naming dialog is confirmed.
create_body="$(sed -n '/func createWorkspaceFile(at directory:/,/^    }/p' "$SESSION" | head -n 35)"
printf '%s' "$create_body" | grep -Fq 'presentWorkspaceNameDialog(' \
    || fail "workspace file creation must ask for a name first"
printf '%s' "$create_body" | grep -Fq 'Data().write(to:' \
    && fail "workspace file creation must not write before the name dialog completes"

# Use the window-owned dialog host, which remains valid after a tab rebind.
grep -Fq 'workspace.windowProvider() ?? webView?.window' "$SESSION" \
    || fail "workspace naming dialog must use shared window provider"

# Explicit right-click handling guarantees menus for rows and blank tree space.
grep -Fq 'override func rightMouseDown(with event: NSEvent)' "$SIDEBAR" \
    || fail "workspace tree must handle right-click events directly"

echo "PASS"
