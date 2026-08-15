# 导出 PDF 直接保存 · 打印功能 · 自定义快捷键 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让“导出 PDF”直接保存（不再弹系统打印面板），新增 macOS“打印…”功能（系统打印面板、打印友好配色），修复专注模式 F11 冲突，并在“快捷键”窗口支持自定义快捷键。

**Architecture:** 全部改动集中在 macOS AppKit 侧与共享前端无关（打印友好样式在 `PDFGenerator` 渲染时注入，不改 `packages/editor-web`）。导出与打印共用 `PDFGenerator` 打印管线：导出用 `showsPanel=false` + `jobDisposition=.save` 直接落盘；打印用 `showsPanel=true` + 系统默认纸张 + 浅色覆盖。自定义快捷键由 `ShortcutSettings`（UserDefaults 持久化）+ `ShortcutCatalog`（命令目录）驱动，`NativeMenuBuilder.commandItem` 建菜单时查询覆盖，`ShortcutWindowController` 提供可编辑 UI。

**Tech Stack:** Swift 5.9 / AppKit / WKWebView / NSPrintOperation / SwiftPM；无前端改动。

## Global Constraints

- 共享前端 `packages/editor-web` 本计划不改动；若执行中发现必须改前端，先停下与用户确认。
- SwiftPM 包没有测试目标（`apps/macos/Package.swift` 只有 executable target），本计划不新增测试目标（超出已批准设计）；每个任务以“构建成功 + 运行时/UI 自动化验证”作为验收，命令与期望值写在各任务步骤里。
- 打印视图生命周期：必须继续使用共享打印宿主 `PrintHost`，任何路径都不得在打印操作未结束时销毁打印窗口/WebView（否则 over-release 崩溃）。
- 文案与 changelog 四语言同步（简体中文/繁体中文/English/日本語），changelog 句末统一句号、不提及版本号更新。
- 每个任务独立提交，提交信息前缀 `feat(macos):` / `fix(macos):`。
- 设计规格：`docs/superpowers/specs/2026-08-15-export-print-shortcuts-design.md`。
- 构建命令（在 `apps/macos` 下执行）：
  ```bash
  PATH=/Users/nabian/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:/private/tmp/markleaf-toolchain:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin \
  SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
  CLANG_MODULE_CACHE_PATH=/private/tmp/markleaf-clang-cache \
  SWIFT_MODULE_CACHE_PATH=/private/tmp/markleaf-swift-cache \
  bash script/build_and_run.sh --build-only
  ```

---

### Task 1: 导出 PDF 直接保存（去掉打印面板）

**Files:**
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession+Export.swift`（`handleExportedContent` 的 PDF 分支）
- Modify: `apps/macos/Sources/MarkLeaf/Services/PDFGenerator.swift`（`runPrintPanel` 中进度面板开关）
- Modify: `apps/macos/Sources/MarkLeaf/Services/L10n.swift`（四语言新增两个键）

**Interfaces:**
- Consumes: 现有 `PDFGenerator().printPDF(html:paperSize:landscape:margins:window:showsPanel:saveURL:completion:)`（已支持 `showsPanel=false` + `saveURL`）。
- Produces: 无新对外签名。

- [ ] **Step 1: 修改 PDF 导出分支为直接保存**

将 `EditorSession+Export.swift` 中 `handleExportedContent` 的 PDF 分支整体替换为：

```swift
        if context.options.format == "pdf" {
            statusText = L10n.t("正在生成 PDF…")
            AppLog.info("开始 PDF 导出（直接保存，纸张 \(context.options.paperSize.rawValue)）")
            guard let window = webView?.window else {
                presentError(L10n.t("无法导出 PDF"))
                onExportComplete?(false)
                return
            }
            PDFGenerator().printPDF(
                html: html,
                paperSize: context.options.paperSize,
                landscape: context.options.landscape,
                margins: context.options.margins,
                window: window,
                showsPanel: false,
                saveURL: context.saveURL
            ) { [weak self] result in
                DispatchQueue.main.async {
                    switch result {
                    case .success(let printed):
                        if printed {
                            self?.statusText = L10n.t("已导出 PDF")
                            AppLog.info("PDF 已导出: \(context.saveURL.path)")
                            self?.onExportComplete?(true)
                        } else {
                            self?.statusText = ""
                            self?.onExportComplete?(false)
                        }
                    case .failure(let error):
                        self?.presentError("PDF 导出失败：\(error.localizedDescription)")
                        self?.onExportComplete?(false)
                    }
                }
            }
        }
```

- [ ] **Step 2: 直接保存时不显示打印进度面板**

在 `PDFGenerator.swift` 的 `runPrintPanel` 中，把：

```swift
            operation.showsProgressPanel = true
```

改为：

```swift
            operation.showsProgressPanel = self.showsPrintPanel
```

- [ ] **Step 3: 新增四语言文案**

在 `L10n.swift` 四个语言的字典中分别加入：

| Key | zh-Hans | zh-Hant | en | ja |
| --- | --- | --- | --- | --- |
| `正在生成 PDF…` | 正在生成 PDF… | 正在產生 PDF… | Generating PDF… | PDF を生成しています… |
| `无法导出 PDF` | 无法导出 PDF | 無法匯出 PDF | Cannot export PDF | PDF を書き出せません |

示例（zh-Hans 字典，其余三个字典同样插入各自译文）：

```swift
        "正在生成 PDF…": "正在生成 PDF…",
        "无法导出 PDF": "无法导出 PDF",
```

- [ ] **Step 4: 构建**

Run: `bash script/build_and_run.sh --build-only`（Global Constraints 中的环境变量）
Expected: `[build] ... MarkLeaf.app 打包完成`

- [ ] **Step 5: 无头回归（多页分页不变）**

Run:
```bash
pkill -x MarkLeaf; open -n /Applications/MarkLeaf.app --args --print-pdf /private/tmp/plan-export.pdf --open /private/tmp/markleaf-long.md
sleep 30; tail -5 /tmp/markleaf-app.log
```
Expected: 日志含 `--print-pdf 完成: 成功`，`/private/tmp/plan-export.pdf` 存在且 >1 页（用 `mdls -name kMDItemNumberOfPages` 或 pypdf 检查）。

- [ ] **Step 6: UI 验证（无打印面板）**

启动应用打开测试文档，执行 文件 → 导出… → 选 PDF → 保存；观察全程不出现系统打印面板，状态栏出现“正在生成 PDF…”，文件生成后状态栏为“已导出 PDF”。

- [ ] **Step 7: Commit**

```bash
git add apps/macos/Sources/MarkLeaf/Services/EditorSession+Export.swift apps/macos/Sources/MarkLeaf/Services/PDFGenerator.swift apps/macos/Sources/MarkLeaf/Services/L10n.swift
git commit -m "feat(macos): export PDF directly to file without the print panel"
```

---

### Task 2: 新增“打印…”功能

**Files:**
- Modify: `apps/macos/Sources/MarkLeaf/Models/EditorModels.swift`（`ExportContext` 加 `forPrint`）
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession.swift`（`isExportingOrPrinting`、`printDocument()`、`performMenuCommand` case）
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession+Export.swift`（`runExport` 互斥 + `forPrint` 透传、`handleExportedContent` 打印分支）
- Modify: `apps/macos/Sources/MarkLeaf/Services/PDFGenerator.swift`（`printPDF` 参数 + 打印友好注入 + 系统默认纸张）
- Modify: `apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`（文件菜单“打印…”、`validateMenuItem`）
- Modify: `apps/macos/Sources/MarkLeaf/Services/L10n.swift`

**Interfaces:**
- Produces（后续任务依赖）：
  - `struct ExportContext { var options: ExportOptions; var saveURL: URL; var forPrint = false }`
  - `EditorSession.runExport(options: ExportOptions, saveURL: URL, forPrint: Bool = false)`
  - `EditorSession.printDocument()`
  - `PDFGenerator.printPDF(html:paperSize:landscape:margins:window:showsPanel:saveURL:useSystemPaperDefaults:printFriendly:completion:)`
  - 菜单命令 `"print"`

- [ ] **Step 1: ExportContext 加 forPrint**

`EditorModels.swift`：

```swift
/// 导出请求上下文。
struct ExportContext {
    var options: ExportOptions
    var saveURL: URL
    var forPrint = false
}
```

- [ ] **Step 2: PDFGenerator.printPDF 扩展参数与打印友好注入**

`PDFGenerator.swift` 中 `printPDF` 签名改为：

```swift
    func printPDF(
        html: String,
        paperSize: PaperSize,
        landscape: Bool,
        margins: ExportMargins,
        window: NSWindow,
        showsPanel: Bool = true,
        saveURL: URL? = nil,
        useSystemPaperDefaults: Bool = false,
        printFriendly: Bool = false,
        completion: @escaping (Result<Bool, Error>) -> Void
    )
```

函数体内，把创建 `NSPrintInfo` 与页面尺寸的部分替换为：

```swift
        let info = NSPrintInfo()
        info.topMargin = margins.top
        info.bottomMargin = margins.bottom
        info.leftMargin = margins.left
        info.rightMargin = margins.right
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false
        if !useSystemPaperDefaults {
            let size = paperSize.sizeInches
            let pointsPerInch: CGFloat = 72
            info.paperSize = NSSize(
                width: (landscape ? size.height : size.width) * pointsPerInch,
                height: (landscape ? size.width : size.height) * pointsPerInch)
            info.orientation = landscape ? .landscape : .portrait
        }
        if !showsPanel, let saveURL {
            info.jobDisposition = .save
            info.dictionary()[NSPrintInfo.AttributeKey.jobSavingURL] = saveURL
        }
        self.printInfo = info

        // 初始帧按一页大小；printOperation(with:) 会自行按打印信息跨页分页。
        // 复用共享打印宿主窗口/WebView：打印操作在取消后仍可能引用打印视图，
        // 若在此销毁视图会触发 over-release 崩溃（SIGSEGV）。
        let pageWidth = info.paperSize.width
        let pageHeight = info.paperSize.height
        let host = PrintHost.shared
        host.window.setContentSize(NSSize(width: pageWidth, height: pageHeight))
        host.webView.frame = NSRect(x: 0, y: 0, width: pageWidth, height: pageHeight)
        let webView = host.webView
        webView.navigationDelegate = self
        self.webView = webView
```

随后把加载 HTML 的部分改为（在原 `fixLocalImagePaths` 基础上叠加打印友好样式）：

```swift
        let printHTML = Self.forcePrintBackgrounds(in: html)
        var adjustedHTML = Self.fixLocalImagePaths(in: printHTML)
        if printFriendly {
            adjustedHTML = Self.forcePrintFriendly(in: adjustedHTML)
        }
        webView.loadHTMLString(adjustedHTML, baseURL: nil)
        AppLog.info("PDFGenerator: 打印 HTML 已加载 (\(html.count) 字符)")
```

在类内新增（放在 `forcePrintBackgrounds` 旁边）：

```swift
    /// 打印友好：无论当前主题，强制白底深字。注入的 `:root` 变量覆盖位于主题 CSS 之后，
    /// 同选择器同优先级下后声明生效，因此能压过导出 HTML 内嵌的主题配色。
    private static func forcePrintFriendly(in html: String) -> String {
        let rule = """
        :root {
          --bg-primary: #FFFFFF;
          --bg-hover: #F3F2F8;
          --bg-selected: #E7E7EF;
          --bg-selected-hover: #E4E2EB;
          --text-primary: #000000;
          --text-secondary: #555555;
          --text-tertiary: #6B6B6B;
          --text-selected: #FFFFFF;
          --theme-light: #0088FE;
          --theme-dark: #0051A8;
          --icon: #0088FE;
          --icon-secondary: #505864;
          --scrollbar-idle: #8B8B8B;
          --scrollbar-active: #636363;
        }
        """
        let style = "<style>\n" + rule + "\n</style>\n"
        if let headRange = html.range(of: "</head>") {
            return html.replacingCharacters(in: headRange, with: style + "</head>")
        }
        return style + html
    }
```

同时把 `runPrintPanel` 中 `operation.showsProgressPanel` 保持为 `self.showsPrintPanel`（Task 1 已改，确认仍在）。

- [ ] **Step 3: runExport 互斥与 forPrint 透传**

`EditorSession+Export.swift` 的 `runExport` 开头改为：

```swift
    func runExport(options: ExportOptions, saveURL: URL, forPrint: Bool = false) {
        guard !isExportingOrPrinting else {
            NSSound.beep()
            statusText = L10n.t("正在打印/导出中…")
            return
        }
        isExportingOrPrinting = true
        let settings = SettingsService.shared.settings
```

并把 `pendingExportContext = ExportContext(options: options, saveURL: saveURL)` 改为：

```swift
        pendingExportContext = ExportContext(options: options, saveURL: saveURL, forPrint: forPrint)
```

`EditorSession.swift` 的属性区新增（注意：`EditorSession+Export.swift` 是另一个文件，`private` 无法跨文件访问，因此这里用 internal 并加注释）：

```swift
    /// 导出/打印互斥标志（internal：由 EditorSession+Export.swift 读写）；
    /// 流程（含打印面板）结束前忽略新的触发。
    var isExportingOrPrinting = false
```

- [ ] **Step 4: handleExportedContent 打印分支与状态复位**

`EditorSession+Export.swift` 的 `handleExportedContent` 开头（`pendingExportContext = nil` 之后）插入打印分支：

```swift
        if context.forPrint {
            statusText = L10n.t("正在打开打印面板…")
            AppLog.info("开始打印（系统打印面板）")
            guard let window = webView?.window else {
                presentError(L10n.t("无法打开打印面板"))
                isExportingOrPrinting = false
                onExportComplete?(false)
                return
            }
            PDFGenerator().printPDF(
                html: html,
                paperSize: .a4,
                landscape: false,
                margins: ExportMargins(),
                window: window,
                showsPanel: true,
                useSystemPaperDefaults: true,
                printFriendly: true
            ) { [weak self] result in
                DispatchQueue.main.async {
                    guard let self else { return }
                    self.isExportingOrPrinting = false
                    switch result {
                    case .success(let printed):
                        if printed {
                            self.statusText = L10n.t("已发送到打印机")
                            AppLog.info("打印任务已提交")
                            self.onExportComplete?(true)
                        } else {
                            self.statusText = ""
                            self.onExportComplete?(false)
                        }
                    case .failure(let error):
                        self.presentError("打印失败：\(error.localizedDescription)")
                        self.onExportComplete?(false)
                    }
                }
            }
        } else if context.options.format == "pdf" {
```

随后对 PDF 分支与 HTML 分支补上互斥复位（三处）：

1. PDF 分支的 `guard let window = webView?.window else` 块改为：

```swift
            guard let window = webView?.window else {
                presentError(L10n.t("无法导出 PDF"))
                isExportingOrPrinting = false
                onExportComplete?(false)
                return
            }
```

2. PDF 分支的 `printPDF` 完成回调开头（`DispatchQueue.main.async {` 内第一行）加：

```swift
                    self?.isExportingOrPrinting = false
```

3. HTML 分支改为：

```swift
        } else {
            do {
                try html.write(to: context.saveURL, atomically: true, encoding: .utf8)
                isExportingOrPrinting = false
                statusText = L10n.t("已导出 HTML")
                AppLog.info("HTML 已导出: \(context.saveURL.path)")
                onExportComplete?(true)
            } catch {
                isExportingOrPrinting = false
                presentError("导出失败：\(error.localizedDescription)")
                onExportComplete?(false)
            }
        }
```

- [ ] **Step 5: printDocument()**

`EditorSession.swift`（或 `EditorSession+Export.swift`）新增：

```swift
    /// 文件 → 打印…：生成打印 HTML 并弹出系统打印面板（纸张/方向跟随系统默认）。
    func printDocument() {
        guard webView?.window != nil else { return }
        var options = ExportOptions()
        options.format = "html"
        options.style = currentStyleId
        options.colorScheme = nil
        let tempURL = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("markleaf-print.html")
        runExport(options: options, saveURL: tempURL, forPrint: true)
    }
```

`performMenuCommand` 的 switch 中加：

```swift
        case "print": printDocument()
```

- [ ] **Step 6: 文件菜单与启用逻辑**

`NativeMenuBuilder.swift` 的 `fileMenu()` 中，在“导出…”之后插入：

```swift
        menu.addItem(commandItem(L10n.t("打印…"), "print", key: "p"))
```

`MenuRouter.validateMenuItem` 的 switch 中加：

```swift
        case "print": return AppWindowManager.shared.activeSession != nil
```

- [ ] **Step 7: 新增四语言文案**

在 `L10n.swift` 四个字典中加入：

| Key | zh-Hans | zh-Hant | en | ja |
| --- | --- | --- | --- | --- |
| `打印…` | 打印… | 列印… | Print… | プリント… |
| `正在打印…` | 正在打印… | 正在列印… | Printing… | 印刷しています… |
| `已发送到打印机` | 已发送到打印机 | 已傳送到印表機 | Sent to printer | プリンターに送信しました |
| `正在打印/导出中…` | 正在打印/导出中… | 正在列印/匯出中… | Printing or exporting… | 印刷または書き出し中です… |

（`正在打开打印面板…` / `无法打开打印面板` 已存在，无需新增。）

- [ ] **Step 8: 构建**

Run: `bash script/build_and_run.sh --build-only`
Expected: 打包完成。

- [ ] **Step 9: UI 验证**

1. 文件 → 打印…（⌘P）：系统打印面板出现，预览为白底黑字（可在面板内确认背景为白色）。
2. 点击“取消”：应用不崩溃（日志 `打印面板结束 success=false`），状态栏恢复为空。
3. 再次打印 → 面板 → PDF 菜单 → “存储为 PDF”：产物为多页 PDF。
4. 导出 → PDF：仍直接保存、无打印面板（回归 Task 1）。

- [ ] **Step 10: Commit**

```bash
git add apps/macos/Sources/MarkLeaf/Models/EditorModels.swift apps/macos/Sources/MarkLeaf/Services/EditorSession.swift apps/macos/Sources/MarkLeaf/Services/EditorSession+Export.swift apps/macos/Sources/MarkLeaf/Services/PDFGenerator.swift apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift apps/macos/Sources/MarkLeaf/Services/L10n.swift
git commit -m "feat(macos): add Print command with print-friendly system print panel"
```

---

### Task 3: 修复专注模式快捷键冲突

**Files:**
- Modify: `apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`（专注模式菜单项）
- Modify: `apps/macos/Sources/MarkLeaf/Views/EditorWindowController.swift`（去掉 F11 监听分支）

**Interfaces:**
- Consumes: 无。
- Produces: 专注模式默认快捷键变为 `⌘⇧F`；`toggleFocusMode` 命令不变。

- [ ] **Step 1: 菜单快捷键改为 ⌘⇧F**

`NativeMenuBuilder.swift` 中：

```swift
        menu.addItem(commandItem(
            L10n.t("专注模式"),
            "toggleFocusMode",
            key: "f",
            mask: [.command, .shift]
        ))
```

替换原 `key: String(UnicodeScalar(NSF11FunctionKey)!), mask: []` 的写法。

- [ ] **Step 2: 移除 F11 窗口级监听**

`EditorWindowController.swift` 的 `handleFocusModeKey` 替换为：

```swift
    /// 返回是否消费按键。专注模式下 Escape=53 退出。
    @discardableResult
    func handleFocusModeKey(keyCode: UInt16) -> Bool {
        if isFocusMode, keyCode == 53 {
            exitFocusMode()
            return true
        }
        return false
    }
```

同步更新 `installFocusModeKeyMonitor` 的注释：仅监听 Escape 退出专注模式，不再拦截 F11。

- [ ] **Step 3: 构建**

Run: `bash script/build_and_run.sh --build-only`
Expected: 打包完成。

- [ ] **Step 4: UI 验证**

1. ⌘⇧F：进入专注模式（状态栏“专注模式已开启”），再次 ⌘⇧F 退出。
2. F11：不再进入/退出专注模式，且不触发系统“显示桌面”以外的异常。
3. 专注模式下按 Esc：退出专注模式。

- [ ] **Step 5: Commit**

```bash
git add apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift apps/macos/Sources/MarkLeaf/Views/EditorWindowController.swift
git commit -m "fix(macos): rebind focus mode to Command-Shift-F to avoid F11 conflict"
```

---

### Task 4: ShortcutSettings 服务与菜单注入

**Files:**
- Create: `apps/macos/Sources/MarkLeaf/Support/ShortcutSettings.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`（`commandItem` 注入）

**Interfaces:**
- Produces：
  - `struct ShortcutEntry { let command: String; let titleKey: String; let defaultKey: String; let defaultMask: NSEvent.ModifierFlags }`
  - `enum ShortcutCatalog { static let entries: [ShortcutEntry]; static func entry(for command: String) -> ShortcutEntry? }`
  - `final class ShortcutSettings`：`static let shared`；`struct Binding: Codable, Equatable { key: String; modifiers: UInt }`；`func binding(for:) -> Binding?`；`func effectiveKey(for:) -> (key: String, mask: NSEvent.ModifierFlags)`；`func set(_ binding: Binding?, for command: String)`；`func resetAll()`
  - `enum ShortcutConflict: Equatable { case none, invalid, systemReserved, duplicate(command: String) }`；`ShortcutSettings.validate(key:mask:for:) -> ShortcutConflict`
  - `enum ShortcutDisplay { static func string(key:mask:) -> String }`
- Consumes: 无。

- [ ] **Step 1: 创建 ShortcutSettings.swift**

```swift
import AppKit

/// 可自定义快捷键的命令目录项。
struct ShortcutEntry {
    let command: String
    let titleKey: String
    let defaultKey: String
    let defaultMask: NSEvent.ModifierFlags
}

/// 快捷键目录：仅包含在原生菜单里带默认快捷键的命令（与「快捷键」窗口一致）。
enum ShortcutCatalog {
    static let entries: [ShortcutEntry] = [
        ShortcutEntry(command: "new", titleKey: "新建文档", defaultKey: "n", defaultMask: [.command]),
        ShortcutEntry(command: "open", titleKey: "打开…", defaultKey: "o", defaultMask: [.command]),
        ShortcutEntry(command: "save", titleKey: "保存", defaultKey: "s", defaultMask: [.command]),
        ShortcutEntry(command: "saveAs", titleKey: "另存为…", defaultKey: "S", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "export", titleKey: "导出…", defaultKey: "e", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "print", titleKey: "打印…", defaultKey: "p", defaultMask: [.command]),
        ShortcutEntry(command: "undo", titleKey: "撤销", defaultKey: "z", defaultMask: [.command]),
        ShortcutEntry(command: "redo", titleKey: "重做", defaultKey: "Z", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "cut", titleKey: "剪切", defaultKey: "x", defaultMask: [.command]),
        ShortcutEntry(command: "copy", titleKey: "拷贝", defaultKey: "c", defaultMask: [.command]),
        ShortcutEntry(command: "paste", titleKey: "粘贴", defaultKey: "v", defaultMask: [.command]),
        ShortcutEntry(command: "find", titleKey: "查找", defaultKey: "f", defaultMask: [.command]),
        ShortcutEntry(command: "replace", titleKey: "替换", defaultKey: "f", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleBold", titleKey: "加粗", defaultKey: "b", defaultMask: [.command]),
        ShortcutEntry(command: "toggleItalic", titleKey: "斜体", defaultKey: "i", defaultMask: [.command]),
        ShortcutEntry(command: "toggleUnderline", titleKey: "下划线", defaultKey: "u", defaultMask: [.command]),
        ShortcutEntry(command: "formatPainter", titleKey: "格式刷", defaultKey: "c", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "formatPainterApply", titleKey: "应用格式刷", defaultKey: "v", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "insertLink", titleKey: "插入超链接…", defaultKey: "k", defaultMask: [.command]),
        ShortcutEntry(command: "promoteHeading", titleKey: "提升标题级别", defaultKey: ".", defaultMask: [.command]),
        ShortcutEntry(command: "demoteHeading", titleKey: "降低标题级别", defaultKey: ",", defaultMask: [.command]),
        ShortcutEntry(command: "sourceMode", titleKey: "源码模式", defaultKey: "u", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleFocusMode", titleKey: "专注模式", defaultKey: "f", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "zoomIn", titleKey: "放大", defaultKey: "=", defaultMask: [.command]),
        ShortcutEntry(command: "zoomOut", titleKey: "缩小", defaultKey: "-", defaultMask: [.command]),
        ShortcutEntry(command: "resetZoom", titleKey: "重置为100%", defaultKey: "0", defaultMask: [.command]),
    ]

    static func entry(for command: String) -> ShortcutEntry? {
        entries.first { $0.command == command }
    }
}

/// 快捷键持久化与校验（macOS）。UserDefaults 键：customShortcuts。
final class ShortcutSettings {
    static let shared = ShortcutSettings()

    struct Binding: Codable, Equatable {
        var key: String
        var modifiers: UInt

        var mask: NSEvent.ModifierFlags {
            NSEvent.ModifierFlags(rawValue: modifiers)
        }
    }

    private static let storageKey = "customShortcuts"
    private var overrides: [String: Binding]

    private init() {
        if let data = UserDefaults.standard.data(forKey: Self.storageKey),
           let decoded = try? JSONDecoder().decode([String: Binding].self, from: data) {
            overrides = decoded
        } else {
            overrides = [:]
        }
    }

    func binding(for command: String) -> Binding? {
        overrides[command]
    }

    /// 菜单项实际使用的快捷键：有自定义用自定义，否则用默认。
    func effectiveKey(for entry: ShortcutEntry) -> (key: String, mask: NSEvent.ModifierFlags) {
        if let binding = overrides[entry.command] {
            return (binding.key, binding.mask)
        }
        return (entry.defaultKey, entry.defaultMask)
    }

    func set(_ binding: Binding?, for command: String) {
        if let binding {
            overrides[command] = binding
        } else {
            overrides.removeValue(forKey: command)
        }
        persist()
    }

    func resetAll() {
        overrides = [:]
        persist()
    }

    private func persist() {
        if let data = try? JSONEncoder().encode(overrides) {
            UserDefaults.standard.set(data, forKey: Self.storageKey)
        }
    }

    /// 校验新组合。key 为 `charactersIgnoringModifiers` 的单字符，mask 为取交集后的修饰键。
    static func validate(key: String, mask: NSEvent.ModifierFlags, for command: String) -> ShortcutConflict {
        let required: NSEvent.ModifierFlags = [.command, .option, .control]
        guard mask.intersection(required) != [] else { return .invalid }
        guard key.count == 1, let scalar = key.unicodeScalars.first, scalar.value < 0xF700,
              scalar.properties.isAlphabetic || scalar.properties.isDigit || scalar.value == 0x20
                || "=,-.".contains(Character(scalar)) else {
            return .invalid
        }
        // 系统高风险组合：⌘Space、⌃⌘F（全屏），以及会被系统菜单抢占的 ⌘Q/⌘W/⌘H/⌥⌘H/⌘M/⌘,。
        let systemCmdKeys: Set<String> = ["q", "w", "h", "m", ","]
        if (key == " " && mask.contains(.command)) ||
            (key.lowercased() == "f" && mask.contains(.control) && mask.contains(.command)) ||
            (mask.contains(.command) && systemCmdKeys.contains(key)) {
            return .systemReserved
        }
        for entry in ShortcutCatalog.entries where entry.command != command {
            let (ek, em) = ShortcutSettings.shared.effectiveKey(for: entry)
            if ek == key && em == mask {
                return .duplicate(command: entry.command)
            }
        }
        return .none
    }
}

enum ShortcutConflict: Equatable {
    case none
    case invalid
    case systemReserved
    case duplicate(command: String)
}

/// 快捷键显示（⌘⇧⌥⌃ + 大写字符）。
enum ShortcutDisplay {
    static func string(key: String, mask: NSEvent.ModifierFlags) -> String {
        var parts: [String] = []
        if mask.contains(.control) { parts.append("⌃") }
        if mask.contains(.option) { parts.append("⌥") }
        if mask.contains(.shift) { parts.append("⇧") }
        if mask.contains(.command) { parts.append("⌘") }
        parts.append(key.uppercased())
        return parts.joined()
    }
}
```

- [ ] **Step 2: commandItem 注入自定义快捷键**

`NativeMenuBuilder.swift` 的 `commandItem` 改为：

```swift
    private func commandItem(_ title: String, _ command: String, key: String = "", mask: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(MenuRouter.performCommand(_:)), keyEquivalent: key)
        item.target = MenuRouter.shared
        item.keyEquivalentModifierMask = mask
        item.representedObject = command
        if let entry = ShortcutCatalog.entry(for: command) {
            let (effectiveKey, effectiveMask) = ShortcutSettings.shared.effectiveKey(for: entry)
            item.keyEquivalent = effectiveKey
            item.keyEquivalentModifierMask = effectiveMask
        }
        return item
    }
```

- [ ] **Step 3: 构建**

Run: `bash script/build_and_run.sh --build-only`
Expected: 打包完成。

- [ ] **Step 4: 运行时冒烟**

启动应用，`defaults write com.markleaf.app customShortcuts -data` 手工注入可跳过；直接验证默认菜单快捷键显示正常（文件 → 打印… 显示 ⌘P，专注模式显示 ⇧⌘F）。

- [ ] **Step 5: Commit**

```bash
git add apps/macos/Sources/MarkLeaf/Support/ShortcutSettings.swift apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift
git commit -m "feat(macos): add persisted shortcut overrides consumed by the native menu"
```

---

### Task 5: “快捷键”窗口可编辑 UI

**Files:**
- Rewrite: `apps/macos/Sources/MarkLeaf/Views/ShortcutWindowController.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/L10n.swift`

**Interfaces:**
- Consumes: `ShortcutCatalog.entries`、`ShortcutSettings.shared`（`effectiveKey/set/resetAll/validate`）、`ShortcutDisplay.string`、`NativeMenuBuilder.refreshIfNeeded()`。
- Produces: 无对外新签名。

- [ ] **Step 1: 重写 ShortcutWindowController**

整体替换 `ShortcutWindowController.swift` 为：

```swift
import AppKit

/// 快捷键窗口：列出可自定义命令，支持录制新快捷键、清除、恢复默认、全部恢复默认。
final class ShortcutWindowController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private let entries = ShortcutCatalog.entries
    private var recordingCommand: String?
    private var recordingMonitor: Any?

    private let tableView = NSTableView()
    private let statusLabel = NSTextField(labelWithString: "")
    private let changeButton = NSButton(title: L10n.t("更改…"), target: nil, action: nil)
    private let clearButton = NSButton(title: L10n.t("清除"), target: nil, action: nil)
    private let restoreButton = NSButton(title: L10n.t("恢复默认"), target: nil, action: nil)
    private let resetAllButton = NSButton(title: L10n.t("全部恢复默认"), target: nil, action: nil)

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 520, height: 480),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("快捷键")
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        buildContent()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func buildContent() {
        guard let window else { return }

        let keyColumn = NSTableColumn(identifier: .init("key"))
        keyColumn.title = L10n.t("快捷键")
        keyColumn.width = 170
        keyColumn.headerCell.alignment = .right
        let descColumn = NSTableColumn(identifier: .init("desc"))
        descColumn.title = L10n.t("功能")
        descColumn.width = 300
        descColumn.headerCell.alignment = .left
        tableView.addTableColumn(keyColumn)
        tableView.addTableColumn(descColumn)
        tableView.dataSource = self
        tableView.delegate = self
        tableView.rowSizeStyle = .medium
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.allowsMultipleSelection = false

        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false

        statusLabel.font = .systemFont(ofSize: 12)
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.translatesAutoresizingMaskIntoConstraints = false

        changeButton.target = self
        changeButton.action = #selector(startRecording)
        clearButton.target = self
        clearButton.action = #selector(clearShortcut)
        restoreButton.target = self
        restoreButton.action = #selector(restoreDefault)
        resetAllButton.target = self
        resetAllButton.action = #selector(resetAll)
        let doneButton = NSButton(title: L10n.t("好"), target: self, action: #selector(closeWindow))
        doneButton.keyEquivalent = "\r"

        let buttonRow = NSStackView(views: [changeButton, clearButton, restoreButton, resetAllButton])
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 8
        buttonRow.translatesAutoresizingMaskIntoConstraints = false

        let root = NSView()
        root.addSubview(scroll)
        root.addSubview(statusLabel)
        root.addSubview(buttonRow)
        root.addSubview(doneButton)
        for view in [scroll, statusLabel, buttonRow, doneButton] {
            view.translatesAutoresizingMaskIntoConstraints = false
        }
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            scroll.bottomAnchor.constraint(equalTo: statusLabel.topAnchor, constant: -10),
            statusLabel.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            statusLabel.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            statusLabel.bottomAnchor.constraint(equalTo: buttonRow.topAnchor, constant: -8),
            buttonRow.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            buttonRow.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
            doneButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            doneButton.centerYAnchor.constraint(equalTo: buttonRow.centerYAnchor),
            doneButton.widthAnchor.constraint(equalToConstant: 80),
        ])
        window.contentView = root
        updateStatus()
    }

    // MARK: - 表格

    func numberOfRows(in tableView: NSTableView) -> Int {
        entries.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row < entries.count, let column = tableColumn else { return nil }
        let id = NSUserInterfaceItemIdentifier("cell")
        let cell = (tableView.makeView(withIdentifier: id, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = id
            let text = NSTextField(labelWithString: "")
            text.translatesAutoresizingMaskIntoConstraints = false
            text.lineBreakMode = .byTruncatingTail
            cell.addSubview(text)
            cell.textField = text
            NSLayoutConstraint.activate([
                text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
                text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
                text.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        let entry = entries[row]
        if column.identifier.rawValue == "key" {
            let (key, mask) = ShortcutSettings.shared.effectiveKey(for: entry)
            cell.textField?.stringValue = ShortcutDisplay.string(key: key, mask: mask)
            cell.textField?.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
            cell.textField?.alignment = .right
        } else {
            cell.textField?.stringValue = L10n.t(entry.titleKey)
            cell.textField?.font = .systemFont(ofSize: 13)
            cell.textField?.alignment = .left
        }
        return cell
    }

    // MARK: - 录制

    @objc private func startRecording() {
        guard let command = selectedCommand() else {
            statusLabel.stringValue = L10n.t("请先选择要更改的命令")
            return
        }
        recordingCommand = command
        statusLabel.stringValue = L10n.t("请按新快捷键…（Esc 取消）")
        recordingMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.recordingCommand != nil else { return event }
            self.handleRecordedKey(event)
            return nil
        }
    }

    private func handleRecordedKey(_ event: NSEvent) {
        guard let command = recordingCommand else { return }
        recordingCommand = nil
        if let monitor = recordingMonitor {
            NSEvent.removeMonitor(monitor)
            recordingMonitor = nil
        }
        if event.keyCode == 53 { // Esc
            updateStatus()
            return
        }
        let mask = event.modifierFlags.intersection([.command, .option, .control, .shift])
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        switch ShortcutSettings.validate(key: key, mask: mask, for: command) {
        case .none:
            ShortcutSettings.shared.set(
                ShortcutSettings.Binding(key: key, modifiers: mask.rawValue),
                for: command)
            NativeMenuBuilder.refreshIfNeeded()
            tableView.reloadData()
            updateStatus()
        case .invalid:
            statusLabel.stringValue = L10n.t("不支持的快捷键组合")
        case .systemReserved:
            statusLabel.stringValue = L10n.t("该快捷键为系统保留")
        case .duplicate(let other):
            let title = L10n.t(ShortcutCatalog.entry(for: other)?.titleKey ?? other)
            statusLabel.stringValue = L10n.f("快捷键已被「%@」使用", title)
        }
    }

    // MARK: - 操作

    @objc private func clearShortcut() {
        guard let command = selectedCommand() else { return }
        ShortcutSettings.shared.set(nil, for: command)
        NativeMenuBuilder.refreshIfNeeded()
        tableView.reloadData()
        updateStatus()
    }

    @objc private func restoreDefault() {
        clearShortcut()
    }

    @objc private func resetAll() {
        ShortcutSettings.shared.resetAll()
        NativeMenuBuilder.refreshIfNeeded()
        tableView.reloadData()
        updateStatus()
    }

    @objc private func closeWindow() {
        window?.close()
    }

    private func selectedCommand() -> String? {
        guard tableView.selectedRow >= 0, tableView.selectedRow < entries.count else { return nil }
        return entries[tableView.selectedRow].command
    }

    private func updateStatus() {
        statusLabel.stringValue = recordingCommand == nil ? "" : L10n.t("请按新快捷键…（Esc 取消）")
    }
}
```

- [ ] **Step 2: 新增四语言文案**

在 `L10n.swift` 四个字典中加入：

| Key | zh-Hans | zh-Hant | en | ja |
| --- | --- | --- | --- | --- |
| `更改…` | 更改… | 更改… | Change… | 変更… |
| `清除` | 清除 | 清除 | Clear | クリア |
| `恢复默认` | 恢复默认 | 恢復預設 | Restore Default | 既定に戻す |
| `全部恢复默认` | 全部恢复默认 | 全部恢復預設 | Restore All Defaults | すべて既定に戻す |
| `请先选择要更改的命令` | 请先选择要更改的命令 | 請先選擇要更改的命令 | Select a command first | コマンドを選択してください |
| `请按新快捷键…（Esc 取消）` | 请按新快捷键…（Esc 取消） | 請按新快速鍵…（Esc 取消） | Press new shortcut… (Esc to cancel) | 新しいショートカットキーを押してください…（Esc でキャンセル） |
| `不支持的快捷键组合` | 不支持的快捷键组合 | 不支援的快速鍵組合 | Unsupported shortcut | サポートされていないショートカットキーです |
| `该快捷键为系统保留` | 该快捷键为系统保留 | 該快速鍵為系統保留 | This shortcut is reserved by the system | このショートカットキーはシステムで予約されています |
| `快捷键已被「%@」使用` | 快捷键已被「%@」使用 | 快速鍵已被「%@」使用 | Shortcut already used by “%@” | ショートカットキーは「%@」で使用されています |

- [ ] **Step 3: 构建**

Run: `bash script/build_and_run.sh --build-only`
Expected: 打包完成。

- [ ] **Step 4: UI 验证**

1. 帮助 → 快捷键：表格列出全部可自定义命令（含 打印 ⌘P、专注模式 ⇧⌘F）。
2. 选中“保存”行 → 更改… → 按 ⌘⇧S：行内显示新快捷键，主菜单“保存”同步变为 ⌘⇧S。
3. 把“另存为…”改为 ⌘⇧S：提示“快捷键已被「保存」使用”，不生效。
4. 按 F11：提示“不支持的快捷键组合”。
5. 清除/恢复默认/全部恢复默认均生效。
6. 重启应用后自定义仍保留。

- [ ] **Step 5: Commit**

```bash
git add apps/macos/Sources/MarkLeaf/Views/ShortcutWindowController.swift apps/macos/Sources/MarkLeaf/Services/L10n.swift
git commit -m "feat(macos): make shortcut reference window editable with persisted bindings"
```

---

### Task 6: 文案与 Changelog 收尾 · 全量验证 · 发布

**Files:**
- Modify: `apps/macos/Changelog/changelog.zh-Hans.md`、`changelog.zh-Hant.md`、`changelog.en.md`、`changelog.ja.md`

- [ ] **Step 1: 更新四语言 Changelog（1.2.0）**

zh-Hans 的“修复与改进”中，把现有行：

```markdown
- 导出 PDF 改为通过系统打印面板完成：可在面板中完整调整纸张、方向与页边距，再存储为 PDF。
```

替换为：

```markdown
- 导出 PDF 直接保存到所选位置，不再打开系统打印面板。
- 新增“打印…”功能（⌘P）：通过系统打印面板打印当前文档，打印时强制浅色背景与深色文字。
- 专注模式快捷键由 F11 调整为 ⌘⇧F，避免与系统“显示桌面”冲突。
- “快捷键”窗口支持自定义快捷键，即时生效并持久化保存。
```

zh-Hant 的对应替换：

```markdown
- 匯出 PDF 直接儲存到所選位置，不再開啟系統列印面板。
- 新增「列印…」功能（⌘P）：透過系統列印面板列印目前文件，列印時強制淺色背景與深色文字。
- 專注模式快速鍵由 F11 調整為 ⌘⇧F，避免與系統「顯示桌面」衝突。
- 「快速鍵」視窗支援自訂快速鍵，即時生效並持久化儲存。
```

en 的对应替换：

```markdown
- Export PDF now saves directly to the chosen location without opening the system print panel.
- Add a Print… command (⌘P) that prints the current document through the system print panel with a forced light background and dark text.
- Focus Mode now uses ⌘⇧F instead of F11 to avoid conflicting with Show Desktop.
- The Shortcuts window now supports custom shortcuts that apply immediately and persist.
```

ja 的对应替换：

```markdown
- PDF 書き出しは選択した場所に直接保存されるようになり、システム印刷パネルは表示されません。
- 「プリント…」機能（⌘P）を追加：システム印刷パネルで現在の文書を印刷し、印刷時は常に明るい背景と濃い文字を使用します。
- フォーカスモードのショートカットキーを F11 から ⌘⇧F に変更し、システムの「デスクトップを表示」との競合を回避します。
- 「ショートカットキー」ウィンドウでカスタムショートカットを設定でき、即座に反映され永続化されます。
```

四个文件的替换行均以句号结尾，不出现版本号更新字样。

- [ ] **Step 2: 全量 UI 回归**

按顺序验证（每条记录日志/截图）：

1. 导出 HTML：直接保存（回归）。
2. 导出 PDF：直接保存、无打印面板、多页、边距正常。
3. 打印 → 取消：不崩溃；打印 → 存储为 PDF：正常。
4. ⌘⇧F 专注模式开关；F11 无效果。
5. 快捷键窗口：更改即时生效、冲突提示、重启持久化、全部恢复默认。
6. 四语言切换后菜单文案完整（打印…、快捷键窗口按钮）。

- [ ] **Step 3: 前端测试（仅当有前端改动时）**

本计划不改前端，若执行中确有前端改动：`cd packages/editor-web && ./node_modules/.bin/vitest run`，期望 101/101 通过。

- [ ] **Step 4: 构建发布包并安装**

```bash
cd /Users/nabian/Documents/Codex/2026-08-07/wo-e/work/markleaf
PATH=/Users/nabian/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:/private/tmp/markleaf-toolchain:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin \
SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
CLANG_MODULE_CACHE_PATH=/private/tmp/markleaf-clang-cache \
SWIFT_MODULE_CACHE_PATH=/private/tmp/markleaf-swift-cache \
bash apps/macos/script/release/package.sh
```
随后把 DMG 中的 `MarkLeaf.app` 安装到 `/Applications`，`defaults read /Applications/MarkLeaf.app/Contents/Info.plist CFBundleShortVersionString` 输出 `1.2.0`。

- [ ] **Step 5: Commit 并推送**

```bash
git add apps/macos/Changelog
git commit -m "docs(macos): update 1.2.0 changelog for PDF export, print, and shortcuts"
git push origin main
```
