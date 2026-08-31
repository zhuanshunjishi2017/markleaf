#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SETTINGS="$ROOT_DIR/Sources/MarkLeaf/Services/AppSettings.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
PREFS="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"

require() {
  local file="$1" text="$2" message="$3"
  if ! grep -Fq "$text" "$file"; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require_block_contains() {
  local file="$1" start="$2" end="$3" text="$4" message="$5"
  # grep -q 会提前退出触发 pipefail 下的 SIGPIPE；读取全部输入再判断。
  if ! sed -n "/$start/,/$end/p" "$file" | grep -F "$text" > /dev/null; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require "$SETTINGS" 'var workspaceOpenInNewTab = true' \
  'workspace files must default to opening in a new tab'
require "$SETTINGS" 'forKey: .workspaceOpenInNewTab' \
  'the workspace open preference must persist across launches'
require_block_contains "$SESSION" 'func openWorkspaceEntry' 'func moveWorkspaceEntry' \
  'settings.workspaceOpenInNewTab' \
  'workspace entry opening must honor the new-tab preference'
require_block_contains "$SESSION" 'func openWorkspaceEntry' 'func moveWorkspaceEntry' \
  'openDocumentBypassingRouter(at: url)' \
  'current-tab mode must replace in place, not re-enter the new-tab router'
require "$SETTINGS" '在当前标签中打开' \
  'the workspace popup must offer always-current-tab wording'
require "$PREFS" 'workspaceOpenModePopup' \
  'preferences must expose the workspace open mode as a popup'
require "$PREFS" '工作区文件打开方式' \
  'the workspace row label must mirror the external-file row label'
require "$PREFS" 'settings.workspaceOpenInNewTab = WorkspaceFileOpenPreferenceModel.opensInNewTab(' \
  'preference changes must be saved back to settings'

echo "PASS"
