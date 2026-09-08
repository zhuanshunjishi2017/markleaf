#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHORTCUTS="$ROOT_DIR/Sources/MarkLeaf/Support/ShortcutSettings.swift"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$2" "$1" || fail "$1 missing: $2"; }
reject() { grep -Fq "$2" "$1" && fail "$1 unexpectedly contains: $2" || true; }

require "$SHORTCUTS" 'ShortcutEntry(command: "restoreClosedTab", titleKey: "重新打开关闭的标签", defaultKey: "T", defaultMask: [.command, .shift])'
require "$SHORTCUTS" 'ShortcutEntry(command: "toggleTaskList", titleKey: "任务列表", defaultKey: "t", defaultMask: [.command, .option])'
require "$MENU" 'commandItem(L10n.t("重新打开关闭的标签"), "restoreClosedTab")'
require "$MENU" '"restoreClosedTab"'
require "$MENU" 'case "restoreClosedTab":'
require "$MENU" 'restoreLastClosedTab()'
require "$WINDOW" 'func restoreLastClosedTab()'
require "$MANAGER" 'func registerClosedTab(_ record: ClosedTabRecord)'
require "$MANAGER" 'func takeLastClosedTab() -> ClosedTabRecord?'
echo PASS
