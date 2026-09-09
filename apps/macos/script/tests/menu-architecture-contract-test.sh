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
for text in 主题设置 设置缩放; do
  require_text "$mac_view" "$text"
done
for command in toggleCodeHighlight importTheme revealThemeFolder; do
  reject_text "$mac_view" "\"$command\""
done
mac_sidebar="$(printf '%s\n' "$mac_view" | sed -n '/let sidebarSettings = NSMenu/,/menu.addItem(popup(L10n.t("侧栏设置"), sidebarSettings))/p')"
require_text "$mac_sidebar" 'L10n.t("显示侧栏")'
require_text "$mac_sidebar" 'L10n.t("在右侧显示大纲")'
reject_text "$mac_sidebar" 'L10n.t("独立显示大纲")'
mac_sidebar_seq="$(printf '%s\n' "$mac_sidebar" | grep -E 'commandItem|separator' | sed 's/^[[:space:]]*//')"
expected_mac_sidebar_seq=$'sidebarSettings.addItem(commandItem(L10n.t("显示侧栏"), "toggleSidebar"))\nsidebarSettings.addItem(.separator())\nsidebarSettings.addItem(commandItem(L10n.t("工作区"), "workspaceTab"))\nsidebarSettings.addItem(commandItem(L10n.t("大纲"), "outlineTab"))\nsidebarSettings.addItem(commandItem(L10n.t("树结构"), "treeView"))\nsidebarSettings.addItem(commandItem(L10n.t("文档列表"), "listView"))\nsidebarSettings.addItem(.separator())\nsidebarSettings.addItem(commandItem(L10n.t("在右侧显示大纲"), "toggleDetachedOutline"))'
[[ "$mac_sidebar_seq" == "$expected_mac_sidebar_seq" ]] || fail "macOS sidebar submenu order or grouping is incorrect"
require_text "$(method_body "$MAC_MENU" validateMenuItem)" 'SidebarMenuPolicy.leftSidebarContentEnabled'

mac_help="$(method_body "$MAC_MENU" helpMenu)"
reject_text "$mac_help" 'openHomepage'
require_text "$mac_help" 'openHelp'
require_text "$mac_help" 'checkForUpdates'
require_text "$mac_help" 'openWelcome'
mac_help_sequence="$(grep -E 'commandItem|separator' <<<"$mac_help" | sed 's/^[[:space:]]*//')"
expected_mac_help_sequence=$'menu.addItem(commandItem(L10n.t("欢迎"), "openWelcome"))\nmenu.addItem(.separator())\nmenu.addItem(commandItem(L10n.t("快捷键"), "showShortcuts"))\nmenu.addItem(commandItem(L10n.t("更新内容"), "openChangelog"))\nsamples.addItem(commandItem(L10n.t(sample.titleKey), sample.command))\nmenu.addItem(.separator())\nmenu.addItem(commandItem(L10n.t("学习 Markdown…"), "learnMarkdown"))\nmenu.addItem(commandItem(L10n.t("安装可选字体…"), "installOptionalFonts"))\nmenu.addItem(commandItem(L10n.t("检查更新…"), "checkForUpdates"))\nmenu.addItem(.separator())\nmenu.addItem(commandItem(L10n.t("在线帮助"), "openHelp"))'
[[ "$mac_help_sequence" == "$expected_mac_help_sequence" ]] || fail "macOS help menu order or grouping is incorrect"

require_text "$(method_body "$MAC_MENU" editMenu)" 'commandItem(L10n.t("查找与替换"), "find", key: "f")'
require_text "$(method_body "$MAC_MENU" fileMenu)" 'commandItem(L10n.t("新建窗口"), "newWindow", key: "N", mask: [.command, .shift])'
require_text "$MAC_MENU" 'static let zoomOptions = EditorSession.zoomOptions'
require_text "$MAC_SESSION" 'static let zoomOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200]'
require_text "$(sed -n '/headerOpenFolderButton.image = NSImage/,/headerOpenFolderButton.translatesAutoresizingMaskIntoConstraints/p' "$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift")" 'doc.badge.plus'
require_text "$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift" 'SidebarTreePresentation.selectedRowFont'
require_text "$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift" 'locateItem.isEnabled = session'
require_text "$MAC_SHORTCUTS" 'command: "newWindow", titleKey: "新建窗口", defaultKey: "N", defaultMask: [.command, .shift]'
require_text "$MAC_SHORTCUTS" 'command: "find", titleKey: "查找与替换", defaultKey: "f", defaultMask: [.command]'
require_text "$MAC_SHORTCUTS" 'command: "promoteHeading", titleKey: "提升标题级别", defaultKey: ".", defaultMask: [.command, .option]'
require_text "$MAC_SHORTCUTS" 'command: "demoteHeading", titleKey: "降低标题级别", defaultKey: ",", defaultMask: [.command, .option]'
require_text "$MAC_MENU" 'commandItem(L10n.t("高亮"), "toggleHighlight")'
require_text "$MAC_MENU" '"insertAlertNote", "insertAlertTip", "insertAlertImportant"'
require_text "$MAC_MENU" '"insertAlertWarning", "insertAlertCaution"'
require_text "$MAC_MENU" 'commandItem(L10n.t("YAML 前置元数据"), "showFrontMatter")'
require_text "$MAC_MENU" 'L10n.t("最简模式")'
require_text "$MAC_MENU" '"toggleEditorFocusMode"'
require_text "$MAC_MENU" '"toggleTypewriterMode"'
require_text "$MAC_MENU" '"restartEditor"'
require_text "$MAC_MENU" '"learnMarkdown"'
require_text "$MAC_MENU" 'URL(string: "https://markdown.com.cn/basic-syntax/index.html")'
require_text "$MAC_MENU" 'commandItem(L10n.t("HTML"), "copyHtml")'
require_text "$MAC_MENU" '"openSampleAlert"'
require_text "$MAC_MENU" '"openSampleYamlBasic"'
require_text "$MAC_MENU" '"openSampleYamlAdvanced"'
require_text "$MAC_CONTEXT" '"setMathNumber"'
require_text "$MAC_CONTEXT" '"copyHtml"'

for command in rotateImage resizeImage100 saveImageAs; do
  require_text "$MAC_CONTEXT" "\"$command\""
done

# Windows 1.5.1: top-level menus are File/Edit/Paragraph/Format/View/Help.
# The legacy Insert and Appearance menus were removed: block-level insert
# commands moved to the Paragraph menu, inline ones (math/link/image) to the
# Format menu's body and its image submenu.
win_main="$(method_body "$WIN_MENU" BuildMainMenu)"
require_text "$win_main" 'BuildFileMenu()'
require_text "$win_main" 'BuildEditMenu()'
require_text "$win_main" 'BuildParagraphMenu()'
require_text "$win_main" 'BuildFormatMenu()'
require_text "$win_main" 'BuildViewMenu()'
require_text "$win_main" 'BuildHelpMenu()'
reject_text "$win_main" 'BuildInsertMenu()'
reject_text "$win_main" 'BuildAppearanceMenu()'

win_paragraph="$(method_body "$WIN_MENU" BuildParagraphMenu)"
for command in SetParagraph ToggleQuote ToggleCodeBlock ToggleBulletList; do
  require_text "$win_paragraph" "AppCommand.$command"
done
for command in InsertMathBlock InsertHorizontalRule InsertFootnote InsertLineBefore InsertLineAfter \
  InsertMermaid RerenderAllMermaid AddTableRowBefore; do
  require_text "$win_paragraph" "AppCommand.$command"
done
require_text "$win_paragraph" 'AppendPopup(menu, Loc.Get("menu.paragraph.table"), table)'
require_text "$win_paragraph" 'AppendMainMenuCommand(table, AppCommand.InsertTable'
require_text "$win_paragraph" 'AppendPopup(menu, Loc.Get("menu.paragraph.diagram"), diagram)'

win_format="$(method_body "$WIN_MENU" BuildFormatMenu)"
require_text "$win_format" 'AppCommand.InsertMathInline'
require_text "$win_format" 'AppCommand.InsertLink'
require_text "$win_format" 'BuildImageSubmenu()'
require_text "$win_format" 'ClearFormat'
for command in InsertMathBlock InsertHorizontalRule InsertFootnote InsertLineBefore InsertLineAfter \
  InsertTable InsertMermaid SetParagraph ToggleQuote ToggleCodeBlock ToggleBulletList; do
  reject_text "$win_format" "AppCommand.$command"
done
for command in ShowCodeHighlight AddTheme OpenThemeFolder; do
  reject_text "$win_format" "AppCommand.$command"
done
for command in RotateImageClockwise ResizeImage100 SaveImageAs; do
  reject_text "$win_format" "AppCommand.$command"
done

win_image="$(method_body "$WIN_MENU" BuildImageSubmenu)"
for command in InsertImage InsertImageFromUrl RotateImageClockwise SaveImageAs; do
  require_text "$win_image" "AppCommand.$command"
done
require_text "$win_image" 'BuildResizeImageSubmenu()'

win_view="$(method_body "$WIN_MENU" BuildViewMenu)"
for text in menu.view.style menu.view.colorTheme menu.view.zoom; do
  require_text "$win_view" "$text"
done
require_text "$win_view" 'AppCommand.ShowCodeHighlight'
for command in AddTheme OpenThemeFolder; do
  reject_text "$win_view" "AppCommand.$command"
done

win_edit="$(method_body "$WIN_MENU" BuildEditMenu)"
require_text "$win_edit" 'menu.edit.find'
require_text "$win_edit" 'AppCommand.Replace'
require_text "$win_edit" 'AppCommand.SelectAll'
require_text "$WIN_SHORTCUTS" 'new(AppCommand.NewDocument, "shortcut.new", Keys.Control | Keys.N)'
require_text "$WIN_SHORTCUTS" 'new(AppCommand.SelectAll, "shortcut.selectAll", Keys.Control | Keys.A)'

for locale in "$ROOT_DIR"/windows/MarkLeaf/Resources/Locales/*.json; do
  require_text "$locale" '"menu.paragraph.label"'
done

# 空标签/无工作区时的菜单可用性契约（大修）：
mac_file="$(method_body "$MAC_MENU" fileMenu)"
reject_text "$mac_file" '关闭窗口'
require_text "$mac_file" '"closeFolder"'
mac_validate="$(method_body "$MAC_MENU" validateMenuItem)"
require_text "$mac_validate" 'documentIndependentCommands'
require_text "$mac_validate" '(session ?? viewStateSession)?.workspaceRoot != nil'
require_text "$mac_validate" 'NativeTextEditingPolicy.shouldRoute'
mac_router="$(method_body "$MAC_MENU" performCommand)"
require_text "$mac_router" 'controller.newUntitledTab(kind:'
require_text "$mac_router" 'controller.openDocumentPanel()'
# 子菜单父项（图片/插入表格/Mermaid/段落样式/设置缩放/排版样式/复制为）必须挂校验钩子，
# 否则 action=nil 的父项不参与 AppKit 校验，空标签时仍显示可用。
require_text "$MAC_MENU" 'requiresDocument: true'
require_text "$MAC_MENU" '"submenuParent"'
require_text "$ROOT_DIR/macos/Sources/MarkLeaf/Views/TableSizePickerView.swift" '"submenuParent"'

echo "PASS"
