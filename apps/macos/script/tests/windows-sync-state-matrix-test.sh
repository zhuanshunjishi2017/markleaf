#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/EditorMenuPolicy.swift"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"

require() {
  grep -Fq "$2" "$1" || { echo "FAIL: $3" >&2; exit 1; }
}

require "$POLICY" 'static func isCopyHtmlEnabled(' 'copy HTML policy must exist'
require "$POLICY" 'return hasSelection && !isSourceMode' 'copy HTML must require selection and visual mode'
require "$POLICY" 'static func isEditorModeCommandEnabled(' 'editor mode policy must exist'
require "$POLICY" 'return !isReadOnly && !isPlainText' 'editor modes must be disabled for read-only and plain text'

require "$MENU" 'case "copyHtml":' 'copy HTML must use its specialized state case'
require "$MENU" 'EditorMenuPolicy.isCopyHtmlEnabled(' 'menu validation must use the copy HTML policy'
require "$MENU" 'EditorMenuPolicy.isEditorModeCommandEnabled(' 'menu validation must use the editor mode policy'

require "$SESSION" 'guard !isReadOnly, !isPlainText else { return }' 'session commands must enforce the mode guard'
test "$(grep -c 'guard !isReadOnly, !isPlainText else { return }' "$SESSION")" -ge 2 || {
  echo 'FAIL: both focus and typewriter commands need mode guards' >&2
  exit 1
}

echo "PASS"
