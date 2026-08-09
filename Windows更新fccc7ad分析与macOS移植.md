# Windows 更新 fccc7ad 分析与 macOS 移植说明

> 更新日期：2026-08-09
> 上游提交：`fccc7ad`（feat: i18n infrastructure, multi-language support, and UI refinements）
> macOS 分支：`macos-port`

## 一句话总结

Windows 组提交 `fccc7ad` 是一轮以 **i18n 基建 + 界面细化** 为主的大更新（42 个文件、+3506 行）。
其中**可直接/值得移植到 macOS** 的部分（PDF 背景铺满、源码模式西文/中文字体独立选择、
「添加主题」导入 CSS、缩放菜单位置调整）本轮已在 `macos-port` 上实现并验证；
Windows 专属部分（C# i18n 基建、跟随系统深色/菜单栏样式、状态栏 28px 等）在 macOS 上不适用或已等效，未移植。
**重要提醒**：`fccc7ad` 在共享前端上**回退了 macOS 侧的两处关键改动**（主题感知语法高亮、命令驱动的查找面板），
后续合并上游时**切勿直接覆盖** `src/EditorWeb` 下 macOS 侧版本（详见第 4 节）。

---

## 1. fccc7ad 都改了什么

| 模块 | 内容 |
| --- | --- |
| i18n 基建 | 新增 `Loc.cs` + 4 个 JSON locale（zh-CN/zh-TW/en-US/ja-JP，各 350+ 键），28 个 C# 文件本地化，`UiLanguage` 设置（需重启） |
| PDF 导出 | 改用 CSS `@page` 边距，`html { background: var(--bg-primary) }` 让主题背景铺满整页 |
| 颜色模式 | 跟随系统深色模式（读注册表）、菜单栏样式设置（仅深色/始终/跟随系统） |
| 源码字体 | 新增 `SourceFontFamily`（默认 Cascadia Mono）+ `SourceCjkFontFamily`（默认微软雅黑），西文/中文独立选择 |
| 主题 | 新增「添加主题…」按钮（菜单 + 偏好设置），选择 CSS 复制进主题目录并刷新 |
| 缩放 | 缩放菜单从「外观」移到「视图」；缩放偏好移到「编辑器」页；移除缩放下拉与重置按钮 |
| 状态栏 | 高度 28px、深色渲染修复 |
| 其他 | 打开文件夹提示控件、`SidebarCollapsed` 设置、手记样式字体回退（华文楷体→霞鹜文楷）等 |

## 2. 本轮已移植到 macOS 的内容（均已构建验证）

### 2.1 PDF 导出：@page 边距 + 背景铺满（对齐 fccc7ad）
- 文件：`macos/Sources/MarkLeaf/Services/PDFGenerator.swift`
- 实现：导出 HTML 在渲染前注入 `@page { margin: T R B L; background-color: var(--bg-primary) }` 与
  `html { background: var(--bg-primary) }`（优先替换 `</style>`，无则兜底 `<head>`/文档头）。
- 实测说明：macOS 走 `WKWebView.createPDF`（无头），该 API **忽略 `@page` 边距**、按内容高度产出单页，
  因此边距注入在 macOS 上是“尽力而为”的兼容层（不回归、未来 WebKit 若支持即自动生效）；
  主题背景铺满由导出 HTML 已有的 `body { background: var(--bg-primary) }` 承担，与主题 CSS 一起生效。
  已用 `--export-pdf` 自动化验证：注入前后 PDF 渲染完全一致、无回归。

### 2.2 源码模式字体：西文 / 中文独立选择
- 设置：`macos/Sources/MarkLeaf/Services/AppSettings.swift` 新增
  `sourceFontFamily`（默认 Menlo）与 `sourceCjkFontFamily`（默认 PingFang SC），兼容旧配置缺键回退。
- 前端：`src/EditorWeb/src/styles.css` 的 `#source-editor .cm-content, .cm-gutters` 增加
  `font-family: var(--ml-source-font-family, …)`（默认 SF Mono/Menlo/monospace）。
- 下发：`EditorSession.applyVisualVariables` 注入 `--ml-source-font-family`（西文在前、中文在后、monospace 兜底，
  含空格字体名自动加引号），随缩放一起下发。
- UI：偏好设置「编辑器 ▸ 源码模式」新增「西文字体」「中文字体」两行，控件为原生圆角文本框 + 「选择…」按钮
  （调起系统 `NSFontPanel`，新文件 `macos/Sources/MarkLeaf/Views/FontField.swift`）。

### 2.3 「添加主题…」导入 CSS 主题
- 菜单：「外观」菜单新增「添加主题…」（位于「打开主题文件夹…」上方）。
- 偏好设置：「外观」页在「打开主题文件夹…」上方新增「添加主题…」按钮。
- 实现：`EditorSession.importTheme()` 用 `NSOpenPanel` 选择 `.css`，复制到用户主题目录
  （`~/Library/Application Support/MarkLeaf/Themes`），同名文件弹「是否覆盖」确认（覆盖按钮标红），
  成功后 `AppWindowManager.reloadStyles()` 重发样式到各窗口、重建偏好设置与菜单，立即生效（无需重启）。

### 2.4 缩放菜单位置调整（对齐 fccc7ad）
- 菜单：缩放子菜单 + 放大/缩小/重置为 100% 从「外观」移到「视图」（位于源码模式之后）。
- 偏好设置：移除「外观」页的「设置缩放」下拉与「重置为 100%」按钮；
  「打开时还原上次的缩放比例」「使用 ⌘ + 滚轮进行缩放」移到「编辑器」页新增的「缩放视图」分组。

### 2.5 其他小项
- 手记排版样式字体回退对齐上游：`src/MarkLeaf/Resources/Styles/notebook.css`
  （华文楷体/华文仿宋 → 霞鹜文楷，仅影响没有这些字体的环境回退链，两端一致）。

## 3. 明确不移植（及原因）

| fccc7ad 内容 | macOS 处理 |
| --- | --- |
| C# i18n（Loc.cs + JSON） | macOS 已有原生 `L10n.swift`（简体/繁体/英文三语，258+ 键），无需引入 C# 基建；上游新增的日语（ja-JP）如需可后续按 `L10n` 结构补一张表 |
| 跟随系统深色 / 菜单栏样式 | macOS 已通过 `NSApp.appearance` 跟随系统；菜单栏由系统渲染，Windows 的「仅深色/始终/跟随系统」自绘菜单概念不适用 |
| 状态栏 28px | macOS 侧边栏行高已是 28px、状态栏高度由系统布局决定，无需照搬 WinForms 的 MinimumSize |
| `SidebarCollapsed` 等 C# 窗口设置 | macOS 侧边栏显隐已有 `sidebarVisible` 设置与「显示侧栏」命令 |
| 打开文件夹提示控件（OpenFolderPrompt） | macOS 侧边栏已有自己的空工作区提示文案与样式（此前已按你的要求打磨），不重复移植 |

## 4. ⚠️ 合并上游时的关键提醒（重要）

`fccc7ad` 在共享前端 `src/EditorWeb` 上**回退了 macOS 侧依赖的两处改动**：

1. **主题感知语法高亮被回退**：上游删除 `@lezer/highlight` 依赖，把 class-based `HighlightStyle`（`.tok-*`，
   颜色随主题变量，深色主题可读）改回 `defaultHighlightStyle`（非主题硬编码色）。
   macOS 侧必须保留自己的 `main.ts`/`source-editor.ts`/`styles.css`/`package.json` 版本。
2. **命令驱动的查找面板被回退**：上游删除 `findText/findNext/findPrev/replaceOne/replaceAll/findClose/setLanguage`
   命令，改回 HTML 查找栏直读 `findInput.value`。macOS 已用原生 `FindPanelController`（⌘F 浮层面板）替换 HTML 查找栏，
   合并时**必须保留 macOS 侧的 `main.ts`**，否则原生查找面板失效。

**结论**：未来 `git merge origin/main` 时，`src/EditorWeb` 下的冲突应一律以 `macos-port` 为准（`--ours`），
只采纳无害的上游改动（如本次 `styles.css` 的 `--ml-source-font-family` 兜底值、`notebook.css` 字体回退），
并逐项核对后再合入。

## 5. Windows 端回归建议

- 共享前端改动：`src/EditorWeb/src/styles.css`（源码字体变量，对 Windows 是纯增量，默认值兜底为 Cascadia Mono/Consolas）。
- `notebook.css` 字体回退为纯回退链调整，无风险。
- Windows 的「添加主题」「源码字体选择」为 C# 侧实现，两端各自独立，无共享冲突。
