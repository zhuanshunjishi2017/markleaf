#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

require_text "$MENU" 'final class TabManagementMenuDelegate'
require_text "$MENU" 'func menuNeedsUpdate(_ menu: NSMenu)'
require_text "$MENU" '"tabManagement.dynamicTabs"'
require_text "$MENU" '"activateTab:\(tab.tabID.rawValue)"'
require_text "$MENU" 'command.hasPrefix("activateTab:")'
echo PASS
