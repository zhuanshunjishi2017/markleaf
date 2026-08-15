# 导出 PDF 直接保存 · 打印功能 · 自定义快捷键 —— 设计文档

日期：2026-08-15
状态：已获用户确认（2026-08-15）
范围：macOS 端（`apps/macos`）与共享前端（`packages/editor-web`）；Windows 端后续对齐。

## 1. 背景与目标

当前“导出 PDF”会经历两次文件选择、一次参数重复：导出对话框选格式与保存位置 → 生成内容 → 系统打印面板再次选择纸张/方向/边距 → 面板内“存储为 PDF”再选一次位置。同时 App 没有真正的“打印”功能，系统打印面板只服务于导出，名不副实。

目标：

1. “导出 PDF”与“导出 HTML”行为一致：一次选择位置、一次设置参数，直接生成文件，不再弹系统打印面板。
2. 新增真正的“打印…”功能（macOS 先行），走系统打印面板，打印友好（强制浅色背景 + 深色文字）。
3. 修复“专注模式”F11 与系统“显示桌面”的快捷键冲突。
4. 在“快捷键”窗口中支持自定义快捷键，即时生效并持久化。

## 2. 决策记录

- 导出 PDF：**直接保存**（方案 A），实现采用打印管线 `showsPrintPanel = false` + `jobDisposition = .save` + 目标路径直接落盘，而非 `WKWebView.createPDF`（后者正是历史上“只有一页/边距不对”问题的来源）。
- 打印功能：复用 `PDFGenerator.printPDF(showsPanel: true)`，不新建独立管理器。
- 打印配色：**打印友好**，强制白底黑字；排版样式/字号/行距跟随当前编辑器。
- 打印纸张/方向：跟随系统/打印机默认；边距初始“标准”（上下 18、左右 15，单位 point，与现有实现一致）。
- 专注模式默认快捷键：F11 → **⌘⇧F**（退出仍支持 Esc）。
- 自定义快捷键：仅支持含 ⌘/⌥/⌃ 修饰键的组合；不支持 F1–F12；拒绝系统高风险组合（⌘Space、⌃⌘F 等）；App 内部重复组合拒绝。
- 平台策略：打印与自定义快捷键先做 macOS，Windows 后续对齐；共享前端如需改动，保持双端兼容。

## 3. 导出 PDF 直接保存

### 行为

- 导出对话框不变（格式/纸张/方向/页边距/排版样式/配色/页眉页脚）。
- 用户选 PDF、点“保存”后，直接生成 PDF 到所选路径，全程不出现系统打印面板。
- 状态栏文案：“正在生成 PDF…”（替换原“正在打开打印面板…”）。
- 生成期间不弹打印进度面板；失败弹错误提示（沿用现有 `presentError`）。

### 技术改动

- `EditorSession+Export.swift`：PDF 分支由 `printPDF(showsPanel: true)` 改为 `printPDF(showsPanel: false, saveURL: context.saveURL, window: window)`；状态文案与日志更新。
- `PDFGenerator.printPDF`：`showsPanel = false` 时保持现有 `jobDisposition = .save` + `jobSavingURL` 逻辑；关闭 `showsProgressPanel`（状态栏已有反馈）。
- 共享离屏打印宿主（`PrintHost`）保持不变，直接落盘同样复用，规避打印操作 over-release 崩溃。
- 无头验证钩子 `--print-pdf` 继续可用，作为回归测试入口。

## 4. 新增“打印…”功能（macOS）

### 行为

- 菜单：文件 → “打印…”（⌘P）；无文档打开时置灰。
- 点击后生成当前文档的打印 HTML，弹出系统打印面板（自带预览）：
  - 可选择打印机、份数、页码范围、纸张、方向、边距；
  - 可顺手“存储为 PDF”（面板原生能力，非本功能目标）。
- 纸张/方向默认跟随系统/打印机默认（不硬编码 A4）；边距初始“标准”。
- 成功 → 状态栏“已发送到打印机”；取消 → 静默关闭；失败 → 错误弹窗。
- v1 打印整篇文档，不带自定义页眉/页脚（与导出对话框的页眉/页脚无关）。

### 打印友好样式

- 打印 HTML 注入一组覆盖 CSS：将 `--bg-primary`、`--text-primary`、`--text-secondary`、`--bg-hover`、`--bg-selected` 等主题变量固定为白底黑字/浅灰辅助色，不随当前深色主题变化。
- 继续注入 `-webkit-print-color-adjust: exact`，保证输出确定性。
- 排版样式（衬线/等宽、字号、行距、最大宽度）沿用导出 HTML 生成参数，保持版式所见即所得。

### 技术改动

- `NativeMenuBuilder`：文件菜单新增“打印…”（⌘P，`command = "print"`）；启用逻辑：存在活动会话即启用（空文档也允许，打印面板自行处理），无会话时置灰。
- `MenuRouter` / `EditorSession`：新增 `print` 命令 → `session.printDocument()`。
- `EditorSession`：新增 `printDocument()`，复用导出 HTML 生成流程（`execute("exportDocument", ...)`，format=html、无页眉/页脚、传“打印友好”配色 CSS），再调 `PDFGenerator().printPDF(showsPanel: true)`。
- `PDFGenerator`：
  - 新增参数或分支：打印场景使用 `NSPrintInfo()` 默认纸张/方向（跟随系统/打印机），不预设 A4；边距用“标准”预设。
  - 新增“打印友好”CSS 注入函数（与 `forcePrintBackgrounds` 并列）。
- 前端 `packages/editor-web`：导出 HTML 生成支持传入“打印友好”配色（可复用现有 `colorSchemeCss` 参数，传一份内置浅色方案字符串）；不改动视觉模式默认行为。
- 文案：四语言新增“打印… / 正在生成 PDF… / 已发送到打印机 / 正在打印… / 无法打开打印面板”等。
- 快捷键列表（`ShortcutWindowController`）与 Changelog 同步更新。

## 5. 专注模式快捷键冲突修复

### 现状

- 菜单项使用 `NSF11FunctionKey`（AppKit 不允许第三方菜单项声明 function 修饰键），实际由窗口级按键监听捕获 keyCode 103（F11），与系统“显示桌面”冲突。

### 方案

- 默认快捷键改为 **⌘⇧F**（菜单项可直接承载，无需窗口级监听）。
- 移除 F11 的窗口级按键监听逻辑；保留 Esc 退出专注模式的监听。
- 自定义快捷键机制不支持函数键，因此该冲突不会再通过自定义引入。

### 技术改动

- `NativeMenuBuilder`：专注模式菜单项 key 由 `NSF11FunctionKey` 改为 `"f"` + `[.command, .shift]`。
- `EditorWindowController`：`installFocusModeKeyMonitor` / `handleFocusModeKey` 删除 F11 分支，仅保留 Esc 退出。
- 快捷键窗口增加“专注模式 ⌘⇧F”行。

## 6. 自定义快捷键（快捷键窗口）

### 数据模型与持久化

- 新增 `ShortcutSettings`（macOS 侧服务）：
  - UserDefaults 键：`customShortcuts`，值为 `[commandId: {key: String, modifiers: UInt}]`。
  - 提供 `keyEquivalent(for command:) -> (key: String, mask: NSEvent.ModifierFlags)?`，无自定义时返回 nil。
- `NativeMenuBuilder.commandItem` 建菜单时查询 `ShortcutSettings`：有自定义则覆盖默认 key/mask。
- 修改后调用 `NativeMenuBuilder.refreshIfNeeded()` 即时重建菜单；应用重启后从 UserDefaults 恢复。

### UI 交互（`ShortcutWindowController` 改造）

- 表格列：功能 | 当前快捷键 | 操作。
- 点击快捷键单元格进入“录制”状态（单元格显示“请按新快捷键…”），按下组合键后：
  1. 校验规则（见下）；
  2. 保存到 `ShortcutSettings`；
  3. 刷新本表与主菜单。
- 行操作：“清除”（恢复无快捷键）与“恢复默认”；窗口底部“全部恢复默认”。
- 无快捷键行显示“—”。

### 校验规则

- 必须包含 ⌘/⌥/⌃ 至少一个修饰键（仅 ⇧ 不算）。
- 不支持 F1–F12（绕开 AppKit 函数键限制并避免系统冲突）。
- App 内部冲突：新组合已被其他可自定义命令占用 → 拒绝并提示占用方。
- 系统高风险组合拒绝：⌘Space、⌃⌘F（全屏）、⌘Tab 等。
- Esc 视为取消录制。

### 自定义范围

- 所有经 `commandItem` 创建且带默认快捷键的命令（约 20 个），含新增“打印…（⌘P）”与“专注模式（⌘⇧F）”。
- 系统菜单项（偏好设置 ⌘,、退出 ⌘Q、隐藏等）不在自定义范围。

## 7. 边界与错误处理

- 打印/导出互斥：打印面板或导出流程进行中再次触发导出/打印，提示“正在打印/导出中”并忽略本次触发。
- 无文档：打印置灰；导出照旧。
- 打印面板取消/失败：不崩溃（沿用共享打印宿主机制），取消静默、失败弹错。
- 无打印机环境：打印面板仍可打开并“存储为 PDF”（面板原生能力）。
- 快捷键冲突/非法组合：拒绝并提示，不落盘。

## 8. 验证计划

1. 导出 PDF：UI 走“导出 → 保存”，确认无打印面板、生成多页 PDF 且边距正确（可配合 `--print-pdf` 无头回归）。
2. 打印：UI 打开面板，确认预览为白底黑字；取消不崩溃；选“存储为 PDF”产物正常；状态栏文案正确。
3. 专注模式：⌘⇧F 生效，F11 不再触发专注模式。
4. 自定义快捷键：修改即时生效、冲突提示、重启持久化、“恢复默认”生效。
5. 前端测试全量通过，无回归；Swift 构建通过。

## 9. 范围外（后续）

- Windows 端打印功能与自定义快捷键对齐。
- 打印自定义页眉/页脚、打印选区。
- 自定义快捷键支持函数键。
- 每个文档独立的打印设置记忆（当前用系统/打印机默认）。
