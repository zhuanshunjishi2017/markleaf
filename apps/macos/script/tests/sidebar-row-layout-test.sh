#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SIDEBAR="$ROOT_DIR/Sources/MarkLeaf/Views/SidebarView.swift"

require_block_contains() {
  local start="$1" end="$2" text="$3" message="$4"
  # grep -q 提前退出会在 pipefail 下触发 SIGPIPE；读取全部输入再判断。
  if ! sed -n "/$start/,/$end/p" "$SIDEBAR" | grep -F "$text" > /dev/null; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require_block_contains 'final class WorkspaceListCellView' 'MARK: - 大纲树' \
  'nameLabel.trailingAnchor.constraint(lessThanOrEqualTo: timeLabel.leadingAnchor, constant: -6)' \
  'file title must share the first line with the timestamp'
require_block_contains 'final class WorkspaceListCellView' 'MARK: - 大纲树' \
  'timeLabel.centerYAnchor.constraint(equalTo: nameLabel.centerYAnchor)' \
  'the timestamp must stay on the first line instead of wrapping below'
if sed -n '/final class WorkspaceListCellView/,/MARK: - 大纲树/p' "$SIDEBAR" \
   | grep -F 'timeLabel.centerYAnchor.constraint(equalTo: folderLabel.centerYAnchor)' > /dev/null; then
  echo 'FAIL: timestamp must not sit on the secondary line' >&2
  exit 1
fi

require() {
  local file="$1" text="$2" message="$3"
  if ! grep -Fq "$text" "$file"; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

SETTINGS="$ROOT_DIR/Sources/MarkLeaf/Services/AppSettings.swift"
require "$SETTINGS" 'var workspaceWidth = 230' \
  'the workspace sidebar width must stay at its original default'
require "$SETTINGS" 'if workspaceWidth == 260 { workspaceWidth = 230 }' \
  'widths saved by the temporary wider default must restore to the original'

SIDEBAR="$ROOT_DIR/Sources/MarkLeaf/Views/SidebarView.swift"
require "$SIDEBAR" 'lastColumnOnlyAutoresizingStyle' \
  'the single table column must stretch to fill the sidebar width'
require "$SIDEBAR" 'column.resizingMask = .autoresizingMask' \
  'the name column must opt into autoresizing so the row cell spans the panel'

echo "PASS"
