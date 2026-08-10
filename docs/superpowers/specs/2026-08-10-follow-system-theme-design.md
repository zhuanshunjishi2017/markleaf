# 主题「与操作系统同步」设计规格

**日期：** 2026-08-10
**状态：** 已批准（用户确认默认开启）

## 目标

在偏好设置「外观」页新增「与操作系统同步」开关：开启时应用根据系统浅色/深色外观自动使用默认浅色主题与默认深色主题，颜色主题选择置灰不可用；关闭时恢复用户手动选择的主题。参考 PyCharm 的 Theme + Sync with OS 交互。

## 行为

1. 新增设置字段 `AppSettings.followSystemTheme: Bool`，**默认 true**（缺失/旧配置解码为 true）。
2. 开启时：
   - 有效主题 = 系统外观对应的默认主题：浅色 → `colors-white-only`，深色 → `colors-dark`（两者都存在于内置主题时；缺失则回退 `defaultThemeId`）。
   - 自动主题**不写入** `settings.colorTheme`，用户手动主题保持不变。
   - 偏好设置「颜色主题」下拉框置灰不可用；「外观」菜单的主题子菜单项同样禁用；`EditorSession.setTheme` 忽略手动调用。
   - `NSApp.appearance = nil`，窗口/菜单完全跟随系统外观。
   - 监听系统外观变化（`AppleInterfaceThemeChangedNotification`），变化时实时重新解析默认主题并换肤，无需重启。
3. 关闭时：恢复「颜色主题」下拉框可用，立即切回 `settings.colorTheme` 保存的手动主题；`NSApp.appearance` 按所选主题明暗设置（现有行为）。
4. 编辑器滚动条、WebView `color-scheme` 与加载背景在跟随模式下按系统外观（与默认主题明暗一致）生效。

## 默认主题

- 默认浅色主题 ID：`colors-white-only`
- 默认深色主题 ID：`colors-dark`
- 解析函数：`StyleManager.defaultThemeID(forDark: Bool) -> String?`

## 涉及文件

- 修改 `macos/Sources/MarkLeaf/Services/AppSettings.swift`（新字段 + 解码默认）
- 修改 `macos/Sources/MarkLeaf/Services/StyleManager.swift`（默认明/暗主题解析）
- 修改 `macos/Sources/MarkLeaf/Services/EditorSession.swift`（跟随系统解析、系统外观监听、setTheme 守卫、applySystemAppearance）
- 修改 `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`（复选框 + 下拉置灰 + 切换即时生效）
- 修改 `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`（外观菜单主题项在跟随模式下禁用）
- 修改 `macos/Sources/MarkLeaf/Services/L10n.swift`（三语新键）
- 新建测试：`macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift`、`macos/Tests/MarkLeafTests/StyleManagerThemeDefaultsTests.swift`

## 测试

1. `AppSettings.followSystemTheme`：缺失字段解码默认 true；显式 false/true 编解码往返一致。
2. `StyleManager.defaultThemeID(forDark:)`：浅色 → colors-white-only；深色 → colors-dark；主题缺失时回退 defaultThemeId。
3. 全量 `swift test --package-path macos` 与 `swift build` 通过。

## 不做的事（YAGNI）

- 不做三档（跟随系统/浅色/深色）模式选择器。
- 不在跟随模式下持久化自动主题。
- 不改动排版样式（markdownStyle）的跟随行为。
