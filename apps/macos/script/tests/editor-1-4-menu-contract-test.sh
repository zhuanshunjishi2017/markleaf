#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU_FILE="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
CONTEXT_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"
SESSION_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
FOOTNOTE_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+Footnote.swift"
MERMAID_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+Mermaid.swift"
CODE_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+CodeBlock.swift"
L10N_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/L10n.swift"

for file in "$MERMAID_FILE" "$CODE_FILE"; do
  if [[ ! -f "$file" ]]; then
    echo "FAIL: missing native editor 1.4 command file: $file" >&2
    exit 1
  fi
done

require_text() {
  local file="$1"
  local text="$2"
  if ! grep -Fq "$text" "$file"; then
    echo "FAIL: missing contract text in $file: $text" >&2
    exit 1
  fi
}

require_text "$MENU_FILE" 'popup(L10n.t("Mermaid"), mermaidMenu())'
require_text "$MENU_FILE" 'commandItem(L10n.t("插入 Mermaid 图表"), "insertMermaid")'
require_text "$MENU_FILE" 'commandItem(L10n.t("重新渲染所有 Mermaid 图表"), "rerenderAllMermaid")'

for method in \
  'func insertMermaid()' \
  'func editSelectedMermaid()' \
  'func rerenderSelectedMermaid()' \
  'func rerenderAllMermaid()' \
  'func deleteSelectedMermaid()'; do
  require_text "$MERMAID_FILE" "$method"
done

for method in \
  'func declareCodeBlockLanguage()' \
  'func copyEntireCodeBlock()'; do
  require_text "$CODE_FILE" "$method"
done

for method in \
  'func goToFootnoteReference()' \
  'func clearFootnoteReferences()' \
  'func deleteFootnoteDefinition()' \
  'func presentFootnoteReferenceMissingAlert()'; do
  require_text "$FOOTNOTE_FILE" "$method"
done

for command in \
  insertMermaid editMermaid deleteMermaid declareCodeLanguage \
  goToFootnoteReference resetFootnoteLabel clearFootnoteReferences deleteFootnote; do
  require_text "$SESSION_FILE" "\"$command\""
done

require_text "$CONTEXT_FILE" 'L10n.t("声明代码语言…")'
require_text "$CONTEXT_FILE" 'L10n.t("复制整段代码")'
require_text "$CONTEXT_FILE" 'L10n.t("转到引用")'
require_text "$CONTEXT_FILE" 'L10n.t("清空引用")'
require_text "$CONTEXT_FILE" 'L10n.t("删除注释")'

for key in \
  '插入 Mermaid 图表' '重新渲染所有 Mermaid 图表' '编辑 Mermaid 源码' \
  '重新渲染 Mermaid 图表' '删除 Mermaid 图表' '声明代码语言…' \
  '复制整段代码' '代码语言' '输入代码块语言；留空可清除语言声明。' \
  '无法复制整段代码' '转到引用' '清空引用' '删除注释' '找不到引用！'; do
  count="$(grep -Fc "\"$key\":" "$L10N_FILE" || true)"
  if [[ "$count" -ne 3 ]]; then
    echo "FAIL: expected three translated entries for '$key', found $count" >&2
    exit 1
  fi
done

echo "PASS"
