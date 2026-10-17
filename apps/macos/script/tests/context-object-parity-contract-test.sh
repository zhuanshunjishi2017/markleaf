#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTEXT="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"
FOOTNOTE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+Footnote.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

image_block="$(sed -n '/semanticContext == .image/,/semanticContext == .math/p' "$CONTEXT")"
printf '%s\n' "$image_block" | grep -q 'resizeImage50.*' || true
actual_image_order="$(printf '%s\n' "$image_block" | grep -o '"resizeImage\(50\|75\|90\|100\)"' | head -4 | tr '\n' ' ')"
[[ "$actual_image_order" == '"resizeImage50" "resizeImage75" "resizeImage90" "resizeImage100" ' ]] \
  || fail "image context width order must be 50, 75, 90, 100 (got: $actual_image_order)"

math_block="$(sed -n '/semanticContext == .math/,/semanticContext == .codeBlock/p' "$CONTEXT")"
grep -Fq 'isReadOnly' <<<"$math_block" || fail "math context must have a read-only branch"
grep -Fq '"viewMathSource"' <<<"$math_block" || fail "read-only math must offer view source"
grep -Fq '"copy"' <<<"$math_block" || fail "read-only math must offer copy"

mermaid_function="$(sed -n '/func addMermaidCommands/,/^    }$/p' "$CONTEXT")"
grep -Fq '"viewMermaidSource"' <<<"$mermaid_function" || fail "read-only Mermaid must offer view source"
grep -Fq '"copy"' <<<"$mermaid_function" || fail "read-only Mermaid must offer copy"

grep -Fq 'L10n.t("插入注释")' "$CONTEXT" \
  && fail "block menus must use the unified footnote term"
grep -Rq 'L10n.t("已插入注释")\|L10n.t("插入注释")' "$FOOTNOTE" \
  && fail "footnote workflows must use the unified footnote term"

echo PASS
