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
assert 'commandItem(L10n.t("主题设置…"), "showThemeSettings", key: "t", mask: [.option, .shift])' in menu, 'unified menu entry and shortcut missing'
assert 'let styleMenu = ' not in menu and 'let themeMenu = ' not in menu, 'legacy submenus remain'
assert 'private var themeSettingsController: ThemeSettingsWindowController?' in manager
assert 'NSWindow.willCloseNotification' in manager, 'closed editor must refresh the retained settings panels'
assert 'func showThemeSettings()' in manager and 'func refreshThemeSettings()' in manager
assert 'extension EditorSession: ThemeSettingsSession' in editor
assert 'CurrentStyleFontNotice.missingPacks' in window
assert 'currentStyleProvider' in window and 'refreshCurrentStyleNotice()' in window
assert 'refreshThemeSettings()' in (root/'Views/EditorWindowController.swift').read_text()
l10n=(root/'Services/L10n.swift').read_text()
for key in ['主题设置…', '主题设置', '选择后立即应用。', '当前排版“%@”缺少字体包：%@。']:
    assert l10n.count('"'+key+'":') == 3, f'missing translation: {key}'
print('Theme settings integration contracts passed')
PY
