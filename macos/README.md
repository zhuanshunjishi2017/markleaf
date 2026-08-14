# MarkLeaf for macOS

[MarkLeaf](https://github.com/zhuanshunjishi2017/markleaf) 的 macOS 实现：
跨平台 Markdown 可视化编辑器，与 Windows 实现（.NET 10 WinForms + WebView2）共享同一套
TypeScript 编辑器前端（Tiptap/ProseMirror + CodeMirror），功能与行为保持一致。

## 架构

与 Windows 版完全镜像：**AppKit 原生外壳 + WKWebView 承载同一套 Web 编辑器前端**。

| Windows 实现 | macOS 对应 | 状态 |
|---|---|---|
| WinForms `Form` + 控件 | `NSWindow` / `NSWindowController` | ✅ 已实现 |
| Win32 `HMENU`（33 处 P/Invoke） | `NSMenu` 原生菜单栏 | ✅ 已实现 |
| WebView2（`editor.local` 虚拟主机） | WKWebView + `markleaf://` 自定义 scheme | ✅ 已实现 |
| `chrome.webview` 消息桥 | `native-shim.js`（注入 window.chrome.webview） | ✅ 已实现 |
| EditorProtocol v1（JSON 消息） | Swift `EditorSession` 重写 | ✅ 已实现 |
| `FileSystemWatcher` 外部变更 | `ExternalDocumentChangeTracker` + `WorkspaceWatcher`（DispatchSource） | ✅ 已实现 |
| 注册表文件关联 | `FileAssociationService` + `Info.plist` `CFBundleDocumentTypes` | ✅ 已实现 |
| 剪贴板 HTML（CF_HTML） | `NSPasteboard` HTML 类型（`EditorSession+Clipboard`） | ✅ 已实现 |
| WebView2 PrintToPdf | `WKWebView.createPDF`（`PDFGenerator`） | ✅ 已实现 |
| 系统图标 `SHGetFileInfo` | `NSWorkspace.icon(forFile:)` | ✅ 已实现 |
| 崩溃恢复 / 自动保存 | `RecoveryService` + 恢复窗口（本地化） | ✅ 已实现 |

前端（`src/EditorWeb`：Tiptap/ProseMirror + CodeMirror 6）**零改动复用**，
通过注入的 `native-shim.js` 提供 WebView2 兼容的 `window.chrome.webview` API。

## 目录结构

macOS 端与 Windows 端（`src/MarkLeaf/`）按职责平行对应：
`Views/` ↔ `UI/`、`Support/` ↔ `Native/`、`Services/EditorSession` ↔ `Editor/EditorHostController`，
共享前端见根 README 的「项目结构」。

```text
markleaf-macos/
├── Package.swift                    # SwiftPM 可执行目标
├── Changelog/                       # 四语言更新日志（zh-Hans/zh-Hant/en/ja）
├── script/
│   ├── prepare_resources.sh         # 构建前端 + 注入桥 + 生成图标
│   ├── build_and_run.sh             # 一键 构建+打包+运行（插件规范）
│   └── release/
│       ├── package.sh               # 发布打包：构建 .app / ZIP / 品牌 DMG / 校验和
│       ├── create-branded-dmg.sh    # 生成带 Finder 布局的安装 DMG
│       ├── markleaf-dmg-layout.sh   # 应用 Finder 图标位置/窗口尺寸/隐藏侧边栏
│       ├── dmg-assets/              # DMG 背景（SVG 源 + 144 DPI PNG + 应用图标）
│       └── tests/                   # 资产/布局/打包脚本测试
├── Resources/                       # 生成的运行时资源（EditorWeb/Styles/AppIcon）
├── Tests/                           # SwiftPM 单元测试 + 探针
├── .codex/environments/environment.toml   # Codex Run 按钮
└── Sources/MarkLeaf/
    ├── main.swift                   # NSApplication 入口
    ├── App/                         # AppDelegate、窗口管理
    ├── Views/                       # 主窗口、侧边栏、偏好设置、恢复窗口、查找面板、表格/字体对话框
    ├── Services/                    # EditorSession 协议桥、scheme 处理、设置、恢复、工作区、导出
    ├── Models/                      # 文档/工作区模型
    └── Support/                     # 原生菜单、本地化、日志、版本、资源定位
```

## 构建与运行

```bash
# 一次性（构建前端 + 编译 + 打包 .app + 启动）
./script/build_and_run.sh

# 其他模式
./script/build_and_run.sh --logs        # 流式查看日志
./script/build_and_run.sh --telemetry   # 按 subsystem 过滤统一日志
./script/build_and_run.sh --verify      # 启动并验证进程
./script/build_and_run.sh -- --open 文档.md   # 启动时打开文件
./script/build_and_run.sh -- --snapshot /tmp/x.png   # 自动截图验证
./script/build_and_run.sh -- --pdf /tmp/x.pdf       # 自动 PDF 渲染验证
```

### 发布打包（DMG）

```bash
./script/release/package.sh
# 产物输出到 macos/dist/release/：
#   MarkLeaf-<版本>-macos-arm64.{dmg,zip,dSYM.zip} + SHA256SUMS.txt
```

要求：Swift 工具链（CommandLineTools 即可，无需完整 Xcode）、Node/pnpm、网络（首次 pnpm install）。
注意：当前环境无完整 Xcode，SwiftUI 宏（@State 等）不可用，故采用纯 AppKit ——
这也与 WinForms 外壳最接近；将来有完整 Xcode 可平滑引入 SwiftUI 视图。

## 已实现

### 编辑器宿主（对应 C# EditorHostController）
- WKWebView 加载未改动前端，协议握手 `ready → applyStyles → loadDocument → documentLoaded`
- 排版样式 / 颜色主题（深色主题自动切换系统外观）、缩放（⌘+/⌘-/⌘0）、自动隐藏滚动条
- 状态栏实时显示光标块/行列/字符数、文档修改标记（标题圆点）
- 数学公式（LaTeX）渲染：与 Windows 1.2.0 同步，KaTeX 内联/块级公式
- 查找替换：全文查找/替换、大小写与全词选项，关闭查找面板时清除高亮

### 文档
- 新建 / 打开 / 保存 / 另存为、拖放 .md 打开、命令行 `--open` 打开、Finder 双击打开（文件关联）
- 纯文本（.txt）固定源码模式，“视图 > 源码模式”菜单项置灰
- 剪贴板：复制为（格式化/纯文本/Markdown）、粘贴（图片→本地保存并插入 / HTML / 纯文本）
- 导出：**PDF**（exportDocument → 离屏 A4 分页 createPDF）与 HTML（纸张/方向/边距/页眉页脚选项面板）
- 更新日志以只读窗口打开（四语言），不会覆盖当前文档、不触发保存提示

### 工作区与大纲（对应 C# Workspace / Outline）
- 侧边栏「工作区」：NSOutlineView 文件树（原生图标、懒加载子目录、右键菜单、打开文件夹/刷新、按名称/内容搜索）
- 侧边栏「大纲」：标题树，点击跳转（scrollToPosition），编辑器滚动联动，支持标题过滤
- 外部文件变更监控：文件被外部修改时提示（DispatchSource）

### 窗口与应用
- 多窗口（文件 > 新建窗口）+ 窗口菜单动态列表 + 窗口位置/侧边栏宽度记忆
- 偏好设置窗口（外观/编辑器/文件，即时生效并广播到所有窗口，语言切换即时刷新）
- 设置 JSON 持久化（~/Library/Application Support/MarkLeaf/settings.json，原子写入）
- 图片本地资源服务：前端 `assets.local` → shim 重写 → `markleaf-asset://` scheme 读取本地图片
- LaunchServices 文件关联注册、系统外观跟随深色主题、专注模式（F11）
- 崩溃恢复：修改文档的保存/丢弃提示、自动保存快照、本地化恢复窗口
- 表单校验：公式/自定义表格等对话框对输入做严格校验（非空、正整数、最大值约束）
- 发布：`package.sh` 生成品牌 DMG（Finder 图标布局 + 144 DPI @2x 背景 + 隐藏侧边栏）

## 后续规划

1. **阶段 7**：分发 —— App Sandbox + 签名 + 公证
2. 更多：导出 PDF 走系统打印面板（完整纸张/边距控制）、拖放多文件导入、右键菜单

## 已知限制

- PDF 纸张/边距通过离屏页面尺寸近似（createPDF 无边距 API）；交互式打印走系统面板是后续项
- 拖放多文件 / `dropFiles` 的 File 对象无法经 WKScriptMessageHandler 传输（单文件可用）
- DMG 背景图必须保持 **144 DPI**（1280×800 像素 = 640×400 点 @2x）；若 DPI 被重置为 72，
  Finder 会按原生像素尺寸显示，整个背景放大一倍
- Finder 对 DMG 背景不做缩放：窗口被拖窄到小于 640 点时会裁掉背景右侧，
  使居中的 “drag to” 元素看起来偏右，请保持 DMG 窗口默认尺寸
