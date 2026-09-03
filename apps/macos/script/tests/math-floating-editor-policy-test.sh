#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSION_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: missing '$2'" >&2; exit 1; }
}

reject_text() {
  if grep -Fq "$2" "$1"; then
    echo "FAIL: obsolete '$2' must be removed" >&2
    exit 1
  fi
}

require_text "$SESSION_FILE" 'func insertMath(isBlock: Bool)'
require_text "$SESSION_FILE" 'execute(command)'
require_text "$SESSION_FILE" 'func editMath()'
require_text "$SESSION_FILE" 'execute("editMath")'
require_text "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift" 'L10n.t("编辑公式源码")'
require_text "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift" '"editMath"'
require_text "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift" 'L10n.t("行内公式"), "insertMathInline"'
require_text "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift" 'L10n.t("段间公式"), "insertMathBlock"'
reject_text "$SESSION_FILE" 'presentMathInputDialog'
reject_text "$SESSION_FILE" 'MathInputDialogLayout'

echo "PASS"
