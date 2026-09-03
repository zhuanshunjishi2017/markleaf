#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
L10N="$ROOT_DIR/Sources/MarkLeaf/Services/L10n.swift"
UI_FILES=(
  "$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
  "$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
  "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"
  "$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
  "$ROOT_DIR/Sources/MarkLeaf/Services/SampleDocumentResource.swift"
  "$ROOT_DIR/Sources/MarkLeaf/Support/ShortcutSettings.swift"
)

extract_table() {
  local table="$1"
  awk -v table="$table" '
    $0 ~ "private static let " table { found=1; next }
    found && $0 == "    ]" { exit }
    found { print }
  ' "$L10N"
}

require_source_key() {
  local key="$1"
  local found=0
  for file in "${UI_FILES[@]}"; do
    if grep -Fq "L10n.t(\"$key\")" "$file" \
       || grep -Fq "titleKey: \"$key\"" "$file" \
       || grep -Fq "return \"$key\"" "$file"; then
      found=1
      break
    fi
  done
  if [[ "$found" != 1 ]]; then
    echo "FAIL: missing zh-Hans source key in UI: $key" >&2
    exit 1
  fi
}

keys=(
  "学习 Markdown…"
  "高亮"
  "编辑器专注模式"
  "打字机模式"
  "最简模式"
  "最简模式已开启"
  "最简模式已关闭"
  "提示框"
  "备注"
  "提示"
  "重要"
  "警告"
  "注意"
  "YAML 前置元数据"
  "公式编号…"
  "HTML"
  "重启编辑器"
  "示例：提示框"
  "示例：YAML 基础"
  "示例：YAML 进阶"
)

for key in "${keys[@]}"; do
  require_source_key "$key"
done

for table in japaneseTable zhHantTable englishTable; do
  table_body="$(extract_table "$table")"
  for key in "${keys[@]}"; do
    if ! grep -Fq "\"$key\":" <<<"$table_body"; then
      echo "FAIL: $table missing translation for $key" >&2
      exit 1
    fi
  done
done

echo "PASS"
