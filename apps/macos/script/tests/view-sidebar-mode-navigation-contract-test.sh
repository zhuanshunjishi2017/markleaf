#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

validation="$(sed -n '/case "treeView":/,/case "toggleStatusBar":/p' "$MENU")"
grep -Fq 'leftSidebarContentEnabled' <<<"$validation" \
  && fail "tree/list must remain available while the sidebar is hidden"
grep -Fq 'return viewStateSession != nil' <<<"$validation" \
  || fail "tree/list must require a window-level session"

dispatch="$(sed -n '/case "treeView", "listView":/,/case "tabNext":/p' "$MENU")"
grep -Fq 'showWorkspaceView(listMode:' <<<"$dispatch" \
  || fail "tree/list navigation must reveal the workspace before selecting a presentation"
grep -Fq 'command == "listView"' <<<"$dispatch" \
  || fail "tree/list navigation must choose list mode explicitly"

grep -Fq 'func showWorkspaceView(listMode: Bool)' "$SESSION" \
  || fail "session must expose a reveal-then-select workspace mode command"

echo PASS
