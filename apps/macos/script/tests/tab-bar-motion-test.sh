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

require 'animateTabChanges' 'tab reload must animate insertions and removals'
require 'setLifted' 'dragging a tab must expose a lift animation state'
require 'layoutSubtreeIfNeeded' 'tab layout changes must be animated as a group'
require 'shadowOpacity' 'lifted tabs must gain a visible shadow'
require 'tabLift' 'lift animation must use a dedicated animation key'
require 'dragPlaceholder' 'dragging must leave a stable placeholder slot'
require 'draggingCell' 'dragging must keep a floating cell separate from the stack'
require 'setFrameOrigin' 'the lifted tab must follow the pointer continuously'
require 'dragStartOffset' 'the floating tab must preserve the grab point'

echo "PASS"
