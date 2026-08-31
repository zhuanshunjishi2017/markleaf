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
  'nameLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8)' \
  'file title must use the full row width instead of sharing it with the date'
require_block_contains 'final class WorkspaceListCellView' 'MARK: - 大纲树' \
  'timeLabel.centerYAnchor.constraint(equalTo: folderLabel.centerYAnchor)' \
  'the timestamp must live on the secondary line so the title gets the full width'
if sed -n '/final class WorkspaceListCellView/,/MARK: - 大纲树/p' "$SIDEBAR" \
   | grep -F 'timeLabel.centerYAnchor.constraint(equalTo: centerYAnchor)' > /dev/null; then
  echo 'FAIL: timestamp must not be vertically centered against the whole row' >&2
  exit 1
fi

echo "PASS"
