#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TAB_BAR="$ROOT_DIR/Sources/MarkLeaf/Views/TabBarController.swift"

require() {
  local text="$1"
  local message="$2"
  if ! grep -Fq "$text" "$TAB_BAR"; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require_block_contains() {
  local start="$1"
  local end="$2"
  local text="$3"
  local message="$4"
  # grep -q 提前退出会在 pipefail 下触发 SIGPIPE；读取全部输入再判断。
  if ! sed -n "/$start/,/$end/p" "$TAB_BAR" | grep -F "$text" > /dev/null; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

require 'animateTabChanges' 'tab reload must animate insertions and removals'
require 'setLifted' 'dragging a tab must expose a lift animation state'
require 'layoutSubtreeIfNeeded' 'tab layout changes must be animated as a group'
require 'shadowOpacity' 'lifted tabs must gain a visible shadow'
require 'tabLift' 'lift animation must use a dedicated animation key'
require 'dragPlaceholder' 'dragging must leave a stable placeholder slot'
require 'draggingCell' 'dragging must keep a floating cell separate from the stack'
require 'setFrameOrigin' 'the lifted tab must follow the pointer continuously'
require 'dragStartOffset' 'the floating tab must preserve the grab point'
require 'acceptsFirstMouse' 'the first press on a tab must be eligible to start a drag'
require_block_contains \
  'override func mouseDown(with event: NSEvent)' \
  'override func mouseDragged(with event: NSEvent)' \
  'controller?.liftCell(self, animated: true)' \
  'the first press must lift the tab in place immediately'
require_block_contains \
  'override func mouseDragged(with event: NSEvent)' \
  'override func mouseUp(with event: NSEvent)' \
  'controller?.beginReorder(from: self, at: event.locationInWindow)' \
  'a drag past the threshold must start the floating reorder'
require_block_contains \
  'override func mouseUp(with event: NSEvent)' \
  'override func rightMouseDown(with event: NSEvent)' \
  'controller?.endReorder(at: event.locationInWindow)' \
  'a completed drag must finish the reorder lifecycle'
require_block_contains \
  'override func mouseUp(with event: NSEvent)' \
  'override func rightMouseDown(with event: NSEvent)' \
  'controller?.unliftCell(self)' \
  'a short press must return the tab to rest'
require_block_contains \
  'override func mouseUp(with event: NSEvent)' \
  'override func rightMouseDown(with event: NSEvent)' \
  'onActivate?()' \
  'a short press must still activate the tab'
require 'addLocalMonitorForEvents' 'reordering must track events at window level, not via a reparented view'
require_block_contains \
  'func beginReorder(from cell: TabCellView, at windowPoint: NSPoint)' \
  'func dragReorder(to windowPoint: NSPoint)' \
  'dragEventMonitor = NSEvent.addLocalMonitorForEvents' \
  'beginning a reorder must install the window-level drag monitor'
require_block_contains \
  'func endReorder(at windowPoint: NSPoint)' \
  'private func targetIndex(for localPoint: NSPoint)' \
  'NSEvent.removeMonitor' \
  'ending a reorder must remove the drag monitor so state can never stick'

echo "PASS"
