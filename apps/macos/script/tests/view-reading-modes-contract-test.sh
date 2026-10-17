#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

for function_name in toggleEditorFocusMode toggleTypewriterMode; do
  block="$(sed -n "/func $function_name()/,/^[[:space:]]*}$/p" "$SESSION")"
  grep -Fq 'guard !isPlainText else { return }' <<<"$block" \
    || fail "$function_name must pause in plain-text/source documents"
  grep -Fq '!isReadOnly' <<<"$block" \
    && fail "$function_name must remain available for read-only documents"
done

echo PASS
