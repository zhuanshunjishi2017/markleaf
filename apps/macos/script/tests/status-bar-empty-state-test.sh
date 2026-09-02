#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
MODEL="$ROOT_DIR/Sources/MarkLeaf/Services/StatusBarDisplayModel.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"

require() {
  local file="$1" text="$2" message="$3"
  if ! grep -Fq "$text" "$file"; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require_block_contains() {
  local file="$1" start="$2" end="$3" text="$4" message="$5"
  # grep -q 提前退出会在 pipefail 下触发 SIGPIPE；读取全部输入再判断。
  if ! sed -n "/$start/,/$end/p" "$file" | grep -F "$text" > /dev/null; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require "$MODEL" 'enum StatusBarEmptyStatePolicy' \
  'status bar must define an empty-state policy'
require "$WINDOW" 'statusBar.distribution = .fill' \
  'status bar must not use fillProportionally (stretches the lone sidebar toggle)'
require "$WINDOW" 'statusSpacer.setContentHuggingPriority(.defaultLow' \
  'status bar must keep a flexible spacer to absorb slack'
require "$WINDOW" 'statusBar.addView(statusSpacer, in: .leading)' \
  'the flexible spacer must live in the leading gravity area'
require "$MODEL" 'shouldShowDocumentItems(hasActiveTab:' \
  'the policy must decide document-item visibility from tab presence'
require_block_contains "$WINDOW" \
  'private func applyStatusBarContents()' \
  'private func scheduleStatusClearIfNeeded()' \
  'StatusBarEmptyStatePolicy.shouldShowDocumentItems(hasActiveTab:' \
  'status bar refresh must consult the empty-state policy'
require_block_contains "$WINDOW" \
  'private func applyStatusBarContents()' \
  'private func scheduleStatusClearIfNeeded()' \
  'viewToggleButton.isHidden = !status.sidebarToggleVisible' \
  'the sidebar toggle must stay configurable even in the empty state'

echo "PASS"
