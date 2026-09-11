#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/MenuCommandAvailabilityPolicy.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$2" "$1" || fail "$1 missing: $2"; }
reject() { grep -Fq "$2" "$1" && fail "$1 unexpectedly contains: $2" || true; }

require "$POLICY" 'enum MenuCommandAvailabilityPolicy'
WHITELIST="$(sed -n '/private static let documentIndependentCommands/,/^    ]$/p' "$MENU")"
for command in tabNext closeCurrentTab closeOtherTabs revealActiveTabInWorkspace copyActiveTabPath revealActiveTabInFinder; do
  if grep -Fq "\"$command\"" <<<"$WHITELIST"; then
    fail "$command must not be unconditionally document-independent"
  fi
done
require "$MENU" 'MenuCommandAvailabilityPolicy.isTabCommandEnabled'
require "$MENU" 'popup(L10n.t("标签页管理"), tabManagement, requiresDocument: true, validationCommand: "closeCurrentTab")'
require "$MANAGER" 'func openDocumentInNewWindow()'
require "$MANAGER" 'activeWindowController?.window'
reject "$MANAGER" 'guard let session = activeSession, let window = session.webView?.window else { return }'
# 偏好设置属于窗口级命令；关闭最后一个标签后仍应使用窗口保留的会话构建窗口。
require "$MANAGER" 'guard let session = activeViewStateSession else { return nil }'
reject "$MANAGER" 'guard let session = primarySession else { return nil }'
echo PASS
