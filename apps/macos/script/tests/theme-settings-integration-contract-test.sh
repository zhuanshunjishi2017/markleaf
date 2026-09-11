#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
python3 - "$ROOT" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1])/'Sources/MarkLeaf'
menu=(root/'Support/NativeMenuBuilder.swift').read_text()
manager=(root/'App/AppWindowManager.swift').read_text()
editor=(root/'Services/EditorSession.swift').read_text()
window=(root/'Views/OptionalFontsWindowController.swift').read_text()
settings=(root/'Views/ThemeSettingsWindowController.swift').read_text()
model=(root/'Services/ThemeSettingsModel.swift').read_text()
assert 'commandItem(L10n.t("主题设置…"), "showThemeSettings", key: "t", mask: [.option, .shift])' in menu, 'unified menu entry and shortcut missing'
assert 'let styleMenu = ' not in menu and 'let themeMenu = ' not in menu, 'legacy submenus remain'
assert 'private var themeSettingsController: ThemeSettingsWindowController?' in manager
assert 'NSWindow.willCloseNotification' in manager, 'closed editor must refresh the retained settings panels'
assert 'func showThemeSettings()' in manager and 'func refreshThemeSettings()' in manager
assert 'extension EditorSession: ThemeSettingsSession' in editor
# 原生主题窗口可能早于 WKWebView ready 打开；会话构造阶段必须已经加载主题目录。
init_match = __import__('re').search(
    r'init\(workspace: WorkspaceContext = WorkspaceContext\(\)\) \{(?P<body>.*?)\n    \}',
    editor,
    __import__('re').S,
)
assert init_match and 'reloadStyleCatalog()' in init_match.group('body'), \
    'EditorSession must preload styles before the web editor ready event'
assert 'CurrentStyleFontNotice.missingPacks' in window
assert 'currentStyleProvider' in window and 'refreshCurrentStyleNotice()' in window
assert 'refreshThemeSettings()' in (root/'Views/EditorWindowController.swift').read_text()
# 颜色页必须按“浅色 / 深色”分组，并为每个主题显示预览色块。
assert 'ColorThemeRow' in model and 'func colorRows(' in model
assert 'rows(for: "浅色", light)' in model and 'rows(for: "深色", dark)' in model
assert 'isGroupRow' in settings and 'shouldSelectRow' in settings
assert 'ThemeSwatchView(theme: theme)' in settings
# 排版页必须为缺字样式提供可点击徽标，而不是只有页脚提示。
assert 'NSButton(title: L10n.t("缺字体")' in settings
l10n=(root/'Services/L10n.swift').read_text()
for key in ['主题设置…', '主题设置', '选择后立即应用。', '当前排版“%@”缺少字体包：%@。', '缺字体']:
    assert l10n.count('"'+key+'":') == 3, f'missing translation: {key}'
print('Theme settings integration contracts passed')
PY
