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
for command in insertLink insertImage insertImageFromUrl insertMermaid insertMathInline insertMathBlock \
  insertHorizontalRule insertFootnote showFrontMatter; do
  require_text "$mac_insert" "\"$command\""
done
require_text "$mac_insert" 'tableSizePickerSubmenu'
reject_text "$mac_insert" 'mermaidMenu()'

mac_edit="$(method_body "$MAC_MENU" editMenu)"
mac_edit_clipboard_sequence="$(printf '%s\n' "$mac_edit" | sed -n '/L10n.t("剪切")/,/L10n.t("粘贴为纯文本")/p' | grep 'menu.addItem(commandItem' | sed 's/^[[:space:]]*//')"
expected_mac_edit_clipboard_sequence=$'menu.addItem(commandItem(L10n.t("剪切"), "cut", key: "x"))\nmenu.addItem(commandItem(L10n.t("拷贝"), "copy", key: "c"))\nmenu.addItem(commandItem(L10n.t("粘贴"), "paste", key: "v"))\nmenu.addItem(commandItem(L10n.t("粘贴为纯文本"), "pastePlainText", key: "V", mask: [.command, .shift]))'
[[ "$mac_edit_clipboard_sequence" == "$expected_mac_edit_clipboard_sequence" ]] || fail "macOS edit clipboard order is incorrect"

mac_format="$(method_body "$MAC_MENU" formatMenu)"
require_text "$mac_format" 'paragraphStyleMenu()'
require_text "$mac_format" '"toggleBlockquote"'
require_text "$mac_format" '"toggleCodeBlock"'
require_text "$mac_format" 'tableEditingMenu()'
require_text "$mac_format" 'imageEditingMenu()'
require_text "$mac_format" 'clearFormat'
require_text "$mac_format" '"toggleCode"'

mac_image="$(method_body "$MAC_MENU" imageEditingMenu)"
mac_image_commands="$(printf '%s\n' "$mac_image" | grep -o '"changeImage"\|"editImageCaption"\|"resizeImage50"\|"resizeImage75"\|"resizeImage90"\|"resizeImage100"\|"rotateImage"\|"saveImageAs"' | tr '\n' ' ')"
expected_mac_image_commands='"changeImage" "editImageCaption" "resizeImage50" "resizeImage75" "resizeImage90" "resizeImage100" "rotateImage" "saveImageAs"'
[[ "$(sed 's/  */ /g; s/ *$//' <<<"$mac_image_commands")" == "$expected_mac_image_commands" ]] || fail "macOS image editing order is incorrect"
mac_paragraph_actions="$(method_body "$MAC_MENU" paragraphActionsMenu)"
for command in insertLineBefore insertLineAfter duplicateParagraph deleteParagraph; do
  require_text "$mac_paragraph_actions" "\"$command\""
done
for command in toggleCodeHighlight importTheme revealThemeFolder; do
  reject_text "$mac_format" "\"$command\""
done

mac_paragraph_style="$(method_body "$MAC_MENU" paragraphStyleMenu)"
for command in setParagraph 'setHeading\(level)'; do
  require_text "$mac_paragraph_style" "\"$command\""
done
reject_text "$mac_paragraph_style" '"toggleBlockquote"'
reject_text "$mac_paragraph_style" '"toggleCodeBlock"'
mac_lists="$(method_body "$MAC_MENU" listsMenu)"
for command in toggleBulletList toggleOrderedList toggleTaskList; do
  require_text "$mac_lists" "\"$command\""
done

mac_view="$(method_body "$MAC_MENU" viewMenu)"
for text in 'commandItem(L10n.t("显示侧栏"), "toggleSidebar")' 主题设置 设置缩放 显示代码高亮 重新渲染所有 Mermaid 图表 进入全屏幕; do
  require_text "$mac_view" "$text"
done
if [[ "${mac_view%%let sidebarSettings*}" != *'menu.addItem(commandItem(L10n.t("显示侧栏"), "toggleSidebar"))'* ]]; then
  fail "sidebar toggle must be a top-level layout item"
fi
for command in importTheme revealThemeFolder; do
  reject_text "$mac_view" "\"$command\""
done
mac_sidebar="$(printf '%s\n' "$mac_view" | sed -n '/let sidebarSettings = NSMenu/,/menu.addItem(popup(L10n.t("侧栏设置"), sidebarSettings))/p')"
require_text "$mac_sidebar" 'L10n.t("在右侧显示大纲")'
reject_text "$mac_sidebar" 'L10n.t("独立显示大纲")'
mac_sidebar_seq="$(printf '%s\n' "$mac_sidebar" | grep -E 'commandItem|separator' | sed 's/^[[:space:]]*//')"
expected_mac_sidebar_seq=$'sidebarSettings.addItem(commandItem(L10n.t("工作区"), "workspaceTab"))\nsidebarSettings.addItem(commandItem(L10n.t("大纲"), "outlineTab"))\nsidebarSettings.addItem(commandItem(L10n.t("树结构"), "treeView"))\nsidebarSettings.addItem(commandItem(L10n.t("文档列表"), "listView"))\nsidebarSettings.addItem(.separator())\nsidebarSettings.addItem(commandItem(L10n.t("在右侧显示大纲"), "toggleDetachedOutline"))'
[[ "$mac_sidebar_seq" == "$expected_mac_sidebar_seq" ]] || fail "macOS sidebar submenu order or grouping is incorrect"
require_text "$(method_body "$MAC_MENU" validateMenuItem)" 'SidebarMenuPolicy.leftSidebarContentEnabled'

mac_help="$(method_body "$MAC_MENU" helpMenu)"
reject_text "$mac_help" 'openHomepage'
require_text "$mac_help" 'openHelp'
reject_text "$mac_help" 'checkForUpdates'
require_text "$mac_help" 'openWelcome'
mac_help_sequence="$(grep -E 'commandItem|separator' <<<"$mac_help" | sed 's/^[[:space:]]*//')"
expected_mac_help_sequence=$'menu.addItem(commandItem(L10n.t("欢迎"), "openWelcome"))\nsamples.addItem(commandItem(L10n.t(sample.titleKey), sample.command))\nmenu.addItem(.separator())\nmenu.addItem(commandItem(L10n.t("快捷键"), "showShortcuts"))\nmenu.addItem(commandItem(L10n.t("更新内容"), "openChangelog"))\nmenu.addItem(commandItem(L10n.t("学习 Markdown…"), "learnMarkdown"))\nmenu.addItem(commandItem(L10n.t("在线帮助"), "openHelp"))\nmenu.addItem(.separator())\nmenu.addItem(commandItem(L10n.t("安装可选字体…"), "installOptionalFonts"))\nmenu.addItem(commandItem(L10n.t("代码格式化器…"), "codeFormatterManager"))'
[[ "$mac_help_sequence" == "$expected_mac_help_sequence" ]] || fail "macOS help menu order or grouping is incorrect"

require_text "$(method_body "$MAC_MENU" editMenu)" 'commandItem(L10n.t("查找与替换"), "find", key: "f")'
require_text "$(method_body "$MAC_MENU" fileMenu)" 'commandItem(L10n.t("新建窗口"), "newWindow", key: "N", mask: [.command, .shift])'

MAC_TABS="$ROOT_DIR/macos/Sources/MarkLeaf/Views/TabBarController.swift"
MAC_SIDEBAR="$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift"
MAC_MANAGER="$ROOT_DIR/macos/Sources/MarkLeaf/App/AppWindowManager.swift"

context_clipboard="$(method_body "$MAC_CONTEXT" addClipboardCommands)"
context_clipboard_sequence="$(printf '%s\n' "$context_clipboard" | grep -E 'addEnabledCommand|pastePlain|addFormatCommand' | sed 's/^[[:space:]]*//')"
expected_context_clipboard_sequence=$'addEnabledCommand(menu, L10n.t("剪切"), "cut", enabled: canCut)\naddEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: canCopy)\nlet copyAs = NSMenuItem(title: L10n.t("复制为"), action: nil, keyEquivalent: "")\naddEnabledCommand(menu, L10n.t("粘贴"), "paste", enabled: canPaste)\nlet pastePlain = menuItem(L10n.t("粘贴为纯文本"), #selector(pastePlainTextFromClipboardAction(_:)))\ncopyAsMenu.addItem(pastePlain)'
[[ "$context_clipboard_sequence" != *"copyAsMenu.addItem(pastePlain)"* ]] || fail "plain paste must be top-level, not nested in Copy As"
require_text "$context_clipboard" 'menu.addItem(pastePlain)'

block="$(method_body "$MAC_CONTEXT" showBlockMenu)"
require_text "$block" 'case .frontMatter:'

tab_menu="$(method_body "$MAC_TABS" showContextMenu)"
require_text "$tab_menu" 'action: .reopenClosedTab'
require_text "$tab_menu" 'let revealInWorkspace = contextMenuItem'
require_text "$tab_menu" 'let copyPath = contextMenuItem'

workspace_menu="$(method_body "$MAC_SIDEBAR" contextMenu)"
require_text "$workspace_menu" 'if isRoot'
require_text "$workspace_menu" 'return backgroundMenu'
workspace_file_sequence="$(printf '%s\n' "$workspace_menu" | sed -n '/#selector(openEntry/,/#selector(shareEntry/p' | grep -E '#selector\((openEntry|openInNewWindowEntry|copyPath|copyContent|openLocation|renameEntry|deleteEntry|shareEntry)' | sed 's/^[[:space:]]*//')"
expected_workspace_file_sequence=$'menu.addItem(item(L10n.t("打开"), #selector(openEntry(_:)), entry))\nmenu.addItem(item(L10n.t("在新窗口中打开"), #selector(openInNewWindowEntry(_:)), entry))\nmenu.addItem(item(pathTitle, #selector(copyPath(_:)), entry))\nlet copyContent = item(L10n.t("复制文件内容"), #selector(copyContent(_:)), entry)\nmenu.addItem(item(L10n.t("在 Finder 中显示"), #selector(openLocation(_:)), entry))\nmenu.addItem(item(L10n.t("重命名"), #selector(renameEntry(_:)), entry))\nmenu.addItem(item(L10n.t("删除"), #selector(deleteEntry(_:)), entry))\nmenu.addItem(item(L10n.t("分享"), #selector(shareEntry(_:)), entry))'
[[ "$workspace_file_sequence" == "$expected_workspace_file_sequence" ]] || fail "macOS file workspace order is incorrect"

background="$(method_body "$MAC_SIDEBAR" backgroundMenu)"
require_text "$background" '复制工作区路径'
require_text "$background" '在 Finder 中显示'
require_text "$MAC_MANAGER" 'controller(containing: target)'
reject_text "$(method_body "$MAC_MANAGER" openSample)" 'try FileManager.default.removeItem'
require_text "$MAC_MENU" 'static let zoomOptions = EditorSession.zoomOptions'
require_text "$MAC_SESSION" 'static let zoomOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200]'
require_text "$(sed -n '/headerOpenFolderButton.image = NSImage/,/headerOpenFolderButton.translatesAutoresizingMaskIntoConstraints/p' "$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift")" 'plus'
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

# Windows follows the approved six-menu structure. Insert owns creation;
# Format owns paragraph presentation and object editing.
win_main="$(method_body "$WIN_MENU" BuildMainMenu)"
require_text "$win_main" 'BuildFileMenu()'
require_text "$win_main" 'BuildEditMenu()'
require_text "$win_main" 'BuildInsertMenu()'
require_text "$win_main" 'BuildFormatMenu()'
require_text "$win_main" 'BuildViewMenu()'
require_text "$win_main" 'BuildHelpMenu()'
reject_text "$win_main" 'BuildParagraphMenu()'
reject_text "$win_main" 'BuildAppearanceMenu()'

win_insert="$(method_body "$WIN_MENU" BuildInsertMenu)"
require_text "$win_insert" 'BuildInsertImageSubmenu()'
for command in InsertTable InsertMermaid InsertLink InsertFootnote InsertMathInline \
  InsertMathBlock InsertHorizontalRule ShowFrontMatter; do
  require_text "$win_insert" "AppCommand.$command"
done

win_paragraph="$(method_body "$WIN_MENU" BuildParagraphMenu)"
for command in SetParagraph PromoteHeading DemoteHeading; do
  require_text "$win_paragraph" "AppCommand.$command"
done
for command in InsertMathBlock InsertHorizontalRule InsertFootnote InsertLineBefore InsertLineAfter \
  InsertMermaid RerenderAllMermaid AddTableRowBefore; do
  reject_text "$win_paragraph" "AppCommand.$command"
done

win_format="$(method_body "$WIN_MENU" BuildFormatMenu)"
require_text "$win_format" 'AppCommand.ToggleInlineCode'
require_text "$win_format" 'BuildParagraphMenu()'
require_text "$win_format" 'AppCommand.ToggleQuote'
require_text "$win_format" 'AppCommand.ToggleCodeBlock'
require_text "$win_format" 'AppCommand.InsertLineBefore'
require_text "$win_format" 'AppCommand.InsertLineAfter'
require_text "$win_format" 'AppendTableCommands(table, includeClipboard: false)'
require_text "$win_format" 'BuildImageSubmenu()'
require_text "$win_format" 'AppCommand.FormatPainter'
require_text "$win_format" 'ClearFormat'
for command in InsertImage InsertImageFromUrl InsertTable InsertMermaid InsertLink InsertFootnote \
  InsertMathInline InsertMathBlock InsertHorizontalRule ShowFrontMatter; do
  reject_text "$win_format" "AppCommand.$command"
done
for command in ShowCodeHighlight AddTheme OpenThemeFolder; do
  reject_text "$win_format" "AppCommand.$command"
done
for command in RotateImageClockwise ResizeImage100 SaveImageAs; do
  reject_text "$win_format" "AppCommand.$command"
done

win_image="$(method_body "$WIN_MENU" BuildImageSubmenu)"
for command in ChangeImage EditImageCaption RotateImageClockwise SaveImageAs; do
  require_text "$win_image" "AppCommand.$command"
done
require_text "$win_image" 'BuildResizeImageSubmenu()'

win_insert_image="$(method_body "$WIN_MENU" BuildInsertImageSubmenu)"
for command in InsertImage InsertImageFromUrl; do
  require_text "$win_insert_image" "AppCommand.$command"
done

win_view="$(method_body "$WIN_MENU" BuildViewMenu)"
# 上游 1.7.x 把排版样式/颜色主题收进「主题设置」窗口，视图菜单改为：
# 侧栏设置、状态栏、标签页管理、代码高亮、源码模式、全屏、最简模式、
# 编辑器专注/打字机模式、主题设置、缩放、重启编辑器。
for text in menu.view.sidebarSettings menu.view.documentTabs menu.view.zoom menu.view.theme; do
  require_text "$win_view" "$text"
done
require_text "$win_view" 'AppCommand.ShowCodeHighlight'
require_text "$win_view" 'AppCommand.ShowThemeSettings'
require_text "$win_view" 'AppCommand.ToggleEditorFocusMode'
require_text "$win_view" 'AppCommand.ToggleEditorTypewriterMode'
for command in AddTheme OpenThemeFolder; do
  reject_text "$win_view" "AppCommand.$command"
done

win_edit="$(method_body "$WIN_MENU" BuildEditMenu)"
require_text "$win_edit" 'menu.edit.find'
require_text "$win_edit" 'AppCommand.Find'
reject_text "$win_edit" 'AppCommand.Replace'
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
# “最近项目”与“恢复未保存的文件…”单独成组：上下各一条分割线。
mac_file_recent="$(printf '%s\n' "$mac_file" | sed -n '/"openFolder")/,/L10n.t("保存")/p')"
mac_file_recent_seq="$(printf '%s\n' "$mac_file_recent" | grep -E 'addItem' | grep -v '"save"' | sed 's/^[[:space:]]*//')"
expected_mac_file_recent_seq=$'menu.addItem(commandItem(L10n.t("打开文件夹…"), "openFolder"))\nmenu.addItem(.separator())\nmenu.addItem(recentParent)\nmenu.addItem(commandItem(L10n.t("恢复未保存的文件…"), "recoverUnsavedFiles"))\nmenu.addItem(.separator())'
[[ "$mac_file_recent_seq" == "$expected_mac_file_recent_seq" ]] || fail "macOS file menu recent/recovery group is incorrect"
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
require_text "$ROOT_DIR/macos/Sources/MarkLeaf/Views/TableSizePickerView.swift" '"insertTable"'

# 编辑器右键菜单契约：普通文本与源码模式都必须带历史命令和全选，
# 并统一通过 addClipboardCommands / addHistoryCommands 注入快捷键。
mac_context_menu="$(method_body "$MAC_CONTEXT" buildEditorContextMenu)"
require_text "$mac_context_menu" 'addHistoryCommands(menu)'
require_text "$mac_context_menu" 'addClipboardCommands(menu)'
require_text "$mac_context_menu" 'NativeFormatToolbarView('
require_text "$MAC_CONTEXT" 'L10n.t("撤销"), "undo"'
require_text "$MAC_CONTEXT" 'L10n.t("重做"), "redo"'
require_text "$MAC_CONTEXT" 'L10n.t("全选"), "selectAll"'

# 复制内容到剪贴板复制的是编辑器内容，未命名标签也必须可用；路径类命令仍要求本地路径。
mac_tab_policy="$(method_body "$ROOT_DIR/macos/Sources/MarkLeaf/Services/MenuCommandAvailabilityPolicy.swift" isTabCommandEnabled)"
require_text "$mac_tab_policy" 'case "copyActiveFileContents":'
require_text "$mac_tab_policy" 'return state.tabCount > 0'

# “标签页管理”的动态标签行必须整体回收，否则每打开一次菜单都会重复追加。
require_text "$MAC_MENU" 'TabManagementDynamicItem.matches(item, key: dynamicItemKey)'
require_text "$MAC_MENU" 'identifier.hasPrefix("activateTab:")'

# 空文档没有可复制的内容：文件菜单和工作区文件右键菜单一致置灰；
# 文件操作已从标签页右键菜单中移除。
require_text "$MAC_MENU" 'hasContent: windowSession.activeTabSession?.hasContent ?? false'
require_text "$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift" 'copyContent.isEnabled = hasCopyableContent(entry)'
require_text "$ROOT_DIR/macos/Sources/MarkLeaf/Views/SidebarView.swift" 'menu.autoenablesItems = false'

echo "PASS"
