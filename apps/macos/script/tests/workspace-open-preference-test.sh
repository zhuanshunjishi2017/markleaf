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
require "$PREFS" 'workspaceNewTabCheck' \
  'preferences must expose the workspace new-tab toggle'
require "$PREFS" 'checkboxWithTitle: L10n.t("在新标签页中打开")' \
  'the toggle label must stay concise under the labeled workspace-file row'
require "$PREFS" 'settings.workspaceOpenInNewTab = workspaceNewTabCheck.state == .on' \
  'preference changes must be saved back to settings'

echo "PASS"
