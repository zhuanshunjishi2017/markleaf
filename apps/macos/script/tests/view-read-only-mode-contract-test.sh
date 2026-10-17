#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
FRONTEND="$ROOT_DIR/../../packages/editor-web/src/main.ts"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

view_menu="$(sed -n '/private func viewMenu()/,/^    private func helpMenu()/p' "$MENU")"
document_mode="$(sed -n '/L10n.t("源码模式")/,/L10n.t("编辑器专注模式")/p' <<<"$view_menu")"

grep -Fq 'L10n.t("只读模式")' <<<"$document_mode" \
    || fail "the read-only toggle must sit in the document-mode group after source mode"

grep -Fq '"toggleReadOnlyMode"' <<<"$document_mode" \
    || fail "the read-only item must route to toggleReadOnlyMode"

toggle_dispatch_block="$(sed -n '/case "toggleReadOnlyMode":/,/case "learnMarkdown":/p' "$MENU")"
grep -Fq 'session?.toggleReadOnlyMode()' <<<"$toggle_dispatch_block" || {
    echo "FAIL: toggleReadOnlyMode must dispatch to the active editor session" >&2
    exit 1
}

validation_block="$(sed -n '/case "toggleReadOnlyMode":/,/case "restoreClosedTab":/p' "$MENU")"
grep -Fq 'menuItem.state = session.isReadOnly ? .on : .off' <<<"$validation_block" \
    && grep -Fq 'return ReadOnlyModePolicy.action' <<<"$validation_block" || {
    echo "FAIL: toggleReadOnlyMode must expose its checked state" >&2
    exit 1
}

grep -Fq "payload.command === 'setReadOnly'" "$FRONTEND" \
    || fail "the editor must accept a host setReadOnly command"
grep -Fq "setReadOnly" "$ROOT_DIR/../../packages/editor-core/src/host-command-policy.ts" \
    || fail "setReadOnly must remain allowed while already read-only"
grep -Fq "setReadOnly" "$ROOT_DIR/../../packages/editor-core/src/source-editor.ts" \
    || fail "SourceEditor must support runtime read-only changes"

echo PASS
