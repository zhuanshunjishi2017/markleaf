#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
MAC_MENU="$ROOT_DIR/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
MAC_SHORTCUTS="$ROOT_DIR/macos/Sources/MarkLeaf/Support/ShortcutSettings.swift"
MAC_CONTEXT="$ROOT_DIR/macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"
MAC_SESSION="$ROOT_DIR/macos/Sources/MarkLeaf/Services/EditorSession.swift"
WIN_MENU="$ROOT_DIR/windows/MarkLeaf/Native/NativeMenuService.cs"
WIN_SHORTCUTS="$ROOT_DIR/windows/MarkLeaf/Commands/ShortcutCatalog.cs"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

require_text() {
  local target="$1"
  local text="$2"
  local content
  if [[ -f "$target" ]]; then content="$(<"$target")"; else content="$target"; fi
  grep -Fq "$text" <<<"$content" || fail "missing '$text'"
}

reject_text() {
  local target="$1"
  local text="$2"
  local content
  if [[ -f "$target" ]]; then content="$(<"$target")"; else content="$target"; fi
  if grep -Fq "$text" <<<"$content"; then
    fail "unexpected '$text'"
  fi
}

# Extract a Swift or C# method body: from its declaration to the first
# 4-space-indented closing brace (nested braces are indented deeper).
method_body() {
  local file="$1"
  local name="$2"
  awk -v name="$name" '
    $0 ~ ("((private[ ]+)?(static[ ]+)?(func|nint)[ ]+" name "\\()") { inbody=1 }
    inbody {
      print
      if ($0 ~ /^    }/) { exit }
    }
  ' "$file"
}

mac_build="$(method_body "$MAC_MENU" build)"
require_text "$mac_build" 'L10n.t("文件")'
require_text "$mac_build" 'L10n.t("编辑")'
require_text "$mac_build" 'L10n.t("插入")'
require_text "$mac_build" 'L10n.t("格式")'
require_text "$mac_build" 'L10n.t("视图")'
require_text "$mac_build" 'L10n.t("帮助")'
reject_text "$mac_build" 'L10n.t("段落")'
reject_text "$mac_build" 'L10n.t("外观")'

mac_insert="$(method_body "$MAC_MENU" insertMenu)"
for command in insertLink insertImage insertImageFromUrl insertMathInline insertMathBlock \
  insertHorizontalRule insertFootnote insertLineBefore insertLineAfter; do
  require_text "$mac_insert" "\"$command\""
done
require_text "$mac_insert" 'tableSizePickerSubmenu'
require_text "$mac_insert" 'mermaidMenu()'

mac_mermaid="$(method_body "$MAC_MENU" mermaidMenu)"
require_text "$mac_mermaid" '"insertMermaid"'
require_text "$mac_mermaid" '"rerenderAllMermaid"'

mac_format="$(method_body "$MAC_MENU" formatMenu)"
require_text "$mac_format" 'paragraphStyleMenu()'
require_text "$mac_format" 'tableEditingMenu()'
require_text "$mac_format" 'clearFormat'
for command in rotateImage resizeImage saveImageAs toggleCodeHighlight importTheme revealThemeFolder; do
  reject_text "$mac_format" "\"$command\""
done

mac_paragraph_style="$(method_body "$MAC_MENU" paragraphStyleMenu)"
for command in setParagraph toggleBlockquote toggleCodeBlock toggleBulletList; do
  require_text "$mac_paragraph_style" "\"$command\""
done

mac_table_edit="$(method_body "$MAC_MENU" tableEditingMenu)"
require_text "$mac_table_edit" 'addRowBefore'

mac_view="$(method_body "$MAC_MENU" viewMenu)"
for text in 排版样式 颜色主题 设置缩放; do
  require_text "$mac_view" "$text"
done
for command in toggleCodeHighlight importTheme revealThemeFolder; do
  reject_text "$mac_view" "\"$command\""
done

mac_help="$(method_body "$MAC_MENU" helpMenu)"
reject_text "$mac_help" 'openHomepage'
require_text "$mac_help" 'openHelp'
require_text "$mac_help" 'checkForUpdates'

require_text "$(method_body "$MAC_MENU" editMenu)" 'commandItem(L10n.t("查找与替换"), "find", key: "f")'
require_text "$(method_body "$MAC_MENU" fileMenu)" 'commandItem(L10n.t("新建窗口"), "newWindow", key: "N", mask: [.command, .shift])'
require_text "$MAC_MENU" 'static let zoomOptions = EditorSession.zoomOptions'
require_text "$MAC_SESSION" 'static let zoomOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200]'
require_text "$MAC_SHORTCUTS" 'command: "newWindow", titleKey: "新建窗口", defaultKey: "N", defaultMask: [.command, .shift]'
require_text "$MAC_SHORTCUTS" 'command: "find", titleKey: "查找与替换", defaultKey: "f", defaultMask: [.command]'
require_text "$MAC_SHORTCUTS" 'command: "promoteHeading", titleKey: "提升标题级别", defaultKey: ".", defaultMask: [.command, .option]'
require_text "$MAC_SHORTCUTS" 'command: "demoteHeading", titleKey: "降低标题级别", defaultKey: ",", defaultMask: [.command, .option]'

for command in rotateImage resizeImage100 saveImageAs; do
  require_text "$MAC_CONTEXT" "\"$command\""
done

win_main="$(method_body "$WIN_MENU" BuildMainMenu)"
require_text "$win_main" 'BuildInsertMenu()'
require_text "$win_main" 'BuildFormatMenu()'
require_text "$win_main" 'BuildViewMenu()'
require_text "$win_main" 'BuildHelpMenu()'
reject_text "$win_main" 'BuildParagraphMenu()'
reject_text "$win_main" 'BuildAppearanceMenu()'

win_insert="$(method_body "$WIN_MENU" BuildInsertMenu)"
for command in InsertLink InsertImage InsertImageFromUrl InsertMathInline InsertMathBlock \
  InsertHorizontalRule InsertFootnote InsertLineBefore InsertLineAfter InsertMermaid; do
  require_text "$win_insert" "AppCommand.$command"
done
require_text "$win_insert" 'AppendPopup(menu, Loc.Get("menu.insert.table"), tableInsert)'
require_text "$win_insert" 'AppendMainMenuCommand(tableInsert, AppCommand.InsertTable'

win_format="$(method_body "$WIN_MENU" BuildFormatMenu)"
require_text "$win_format" 'BuildParagraphStyleMenu()'
require_text "$win_format" 'BuildTableEditingMenu()'
require_text "$win_format" 'ClearFormat'
for command in RotateImageClockwise ResizeImage100 SaveImageAs ShowCodeHighlight AddTheme OpenThemeFolder; do
  reject_text "$win_format" "AppCommand.$command"
done

win_paragraph_style="$(method_body "$WIN_MENU" BuildParagraphStyleMenu)"
for command in SetParagraph ToggleQuote ToggleCodeBlock ToggleBulletList; do
  require_text "$win_paragraph_style" "AppCommand.$command"
done

win_table_edit="$(method_body "$WIN_MENU" BuildTableEditingMenu)"
require_text "$win_table_edit" 'AddTableRowBefore'

win_view="$(method_body "$WIN_MENU" BuildViewMenu)"
for text in menu.appearance.style menu.appearance.colorTheme menu.appearance.zoom; do
  require_text "$win_view" "$text"
done
for command in ShowCodeHighlight AddTheme OpenThemeFolder; do
  reject_text "$win_view" "AppCommand.$command"
done

win_edit="$(method_body "$WIN_MENU" BuildEditMenu)"
require_text "$win_edit" 'menu.edit.findReplace'
require_text "$win_edit" 'AppCommand.SelectAll'
reject_text "$win_edit" 'AppCommand.Replace'
require_text "$WIN_SHORTCUTS" 'new(AppCommand.NewWindow, "shortcut.newWindow", Keys.Control | Keys.Shift | Keys.N)'
require_text "$WIN_SHORTCUTS" 'new(AppCommand.SelectAll, "shortcut.selectAll", Keys.Control | Keys.A)'

for locale in "$ROOT_DIR"/windows/MarkLeaf/Resources/Locales/*.json; do
  require_text "$locale" '"menu.insert.label"'
done

echo "PASS"
