#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/TabShortcutPolicy.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

require_text "$MENU" 'NSMenu(title: L10n.t("标签页管理"))'
require_text "$MENU" 'L10n.t("下一个标签"), "tabNext"'
require_text "$MENU" 'L10n.t("关闭当前标签"), "closeCurrentTab"'
require_text "$MENU" 'L10n.t("关闭其他标签"), "closeOtherTabs"'
require_text "$MENU" 'L10n.t("在工作区定位"), "revealActiveTabInWorkspace"'
require_text "$MENU" 'case "tabNext":'
require_text "$CONTROLLER" 'func selectNextTab(reverse: Bool = false)'
require_text "$CONTROLLER" 'func closeCurrentTab()'
require_text "$CONTROLLER" 'func revealActiveTabInWorkspace()'
require_text "$POLICY" 'static func numberedTarget'
echo PASS
