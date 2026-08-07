# MarkLeaf for macOS（移植中）

将 Windows 原生 Markdown 编辑器 [MarkLeaf](https://github.com/zhuanshunjishi2017/markleaf)
（.NET 10 WinForms + WebView2 + Tiptap/CodeMirror）移植到 macOS 的工程。

## 架构

与 Windows 版完全镜像：**AppKit 原生外壳 + WKWebView 承载同一套 Web 编辑器前端**。

| Windows 实现 | macOS 对应 | 状态 |
|---|---|---|
| WinForms `Form` + 控件 | `NSWindow` / `NSWindowController` | ✅ 已实现 |
| Win32 `HMENU`（33 处 P/Invoke） | `NSMenu` 原生菜单栏 | ✅ 已实现 |
| WebView2（`editor.local` 虚拟主机） | WKWebView + `markleaf://` 自定义 scheme | ✅ 已实现 |
| `chrome.webview` 消息桥 | `native-shim.js`（注入 window.chrome.webview） | ✅ 已实现 |
| EditorProtocol v1（JSON 消息） | Swift `EditorSession` 重写 | ✅ 已实现 |
| `FileSystemWatcher` 外部变更 | 待办（FSEvents） | ⏳ |
| 注册表文件关联 | `Info.plist` `CFBundleDocumentTypes` | ⏳ |
| 剪贴板 HTML（CF_HTML） | `NSPasteboard` HTML 类型 | ⏳ |
| WebView2 PrintToPdf | `WKWebView.createPDF` | ⏳ |
| 系统图标 `SHGetFileInfo` | `NSWorkspace.icon(forFile:)` | ⏳ |

前端（`src/EditorWeb`：Tiptap/ProseMirror + CodeMirror 6）**零改动复用**，
通过注入的 `native-shim.js` 提供 WebView2 兼容的 `window.chrome.webview` API。

## 目录结构

```text
markleaf-macos/
├── Package.swift                    # SwiftPM 可执行目标
├── script/
│   ├── prepare_resources.sh         # 构建前端 + 注入桥 + 生成图标
│   └── build_and_run.sh             # 一键 构建+打包+运行（插件规范）
├── .codex/environments/environment.toml   # Codex Run 按钮
├── Resources/                       # 生成的运行时资源（EditorWeb/Styles/AppIcon）
└── Sources/MarkLeaf/
    ├── main.swift                   # NSApplication 入口
    ├── App/AppDelegate.swift        # 生命周期、图标、--snapshot/--pdf 验证
    ├── Views/
    │   ├── EditorWindowController.swift   # 主窗口 + 状态栏
    │   └── EditorWebContainerView.swift   # WKWebView 宿主
    ├── Services/
    │   ├── EditorSession.swift      # 协议桥（对应 EditorHostController）
    │   ├── EditorSchemeHandler.swift      # markleaf:// 静态资源
    │   └── StyleManager.swift       # 排版样式/颜色主题（复刻 C# StyleService）
    └── Support/
        ├── NativeMenuBuilder.swift  # NSMenu 菜单栏
        ├── ResourceLocator.swift
        └── AppLog.swift             # os.Logger + 文件日志
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

要求：Swift 工具链（CommandLineTools 即可，无需完整 Xcode）、Node/pnpm、网络（首次 pnpm install）。
注意：当前环境无完整 Xcode，SwiftUI 宏（@State 等）不可用，故采用纯 AppKit ——
这也与 WinForms 外壳最接近；将来有完整 Xcode 可平滑引入 SwiftUI 视图。

## 已实现

### 编辑器宿主（对应 C# EditorHostController）
- WKWebView 加载未改动前端，协议握手 `ready → applyStyles → loadDocument → documentLoaded`
- 排版样式 / 颜色主题（深色主题自动切换系统外观）、缩放（⌘+/⌘-/⌘0）、自动隐藏滚动条
- 状态栏实时显示光标块/行列/字符数、文档修改标记（标题圆点）

### 文档
- 新建 / 打开 / 保存 / 另存为、拖放 .md 打开、命令行 `--open` 打开、Finder 双击打开（文件关联）
- 剪贴板：复制为（格式化/纯文本/Markdown）、粘贴（图片→本地保存并插入 / HTML / 纯文本）
- 导出：**PDF**（exportDocument → 离屏 A4 分页 createPDF）与 HTML（纸张/方向/边距/页眉页脚选项面板）

### 工作区与大纲（对应 C# Workspace / Outline）
- 侧边栏「工作区」：NSOutlineView 文件树（原生图标、懒加载子目录、右键菜单、打开文件夹/刷新）
- 侧边栏「大纲」：标题树，点击跳转（scrollToPosition），编辑器滚动联动

### 窗口与应用
- 多窗口（文件 > 新建窗口）+ 窗口菜单动态列表 + 窗口位置/侧边栏宽度记忆
- 偏好设置窗口（外观/编辑器/文件，即时生效并广播到所有窗口）
- 设置 JSON 持久化（~/Library/Application Support/MarkLeaf/settings.json，原子写入）
- 图片本地资源服务：前端 `assets.local` → shim 重写 → `markleaf-asset://` scheme 读取本地图片
- LaunchServices 文件关联注册、系统外观跟随深色主题

## 移植路线图（剩余）

1. **阶段 6 剩余**：崩溃恢复（RecoveryService + 自动保存快照）、外部文件变更监控（FSEvents/DispatchSource）
2. **阶段 7**：分发 —— App Sandbox + 签名 + 公证（见 build-macos-apps 插件的 signing-entitlements / packaging-notarization）
3. 更多：导出 PDF 走系统打印面板（完整纸张/边距控制）、图片拖放导入、右键菜单

## 已知限制

- PDF 纸张/边距通过离屏页面尺寸近似（createPDF 无边距 API）；交互式打印走系统面板是后续项
- 拖放多文件 / `dropFiles` 的 File 对象无法经 WKScriptMessageHandler 传输（单文件可用）
- 右键上下文菜单未实现（前端已发 contextMenuRequested）
- 外部文件变更监控未实现（Roadmap 阶段 6 剩余）
