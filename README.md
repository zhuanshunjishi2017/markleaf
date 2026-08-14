# MarkLeaf

MarkLeaf 是一款**跨平台**的轻量化 Markdown 可视化编辑器，支持 **Windows 与 macOS**，
提供接近所见即所得的编辑体验。界面风格简约低干扰，同时保留各平台桌面应用的原生系统行为。

## 平台支持

| 平台 | 技术栈 | 代码目录 |
|---|---|---|
| Windows | C# + .NET 10 WinForms + WebView2 | `src/MarkLeaf` |
| macOS | Swift + AppKit + WKWebView | `macos/` |

两个平台共享同一套 TypeScript 编辑器前端（Tiptap/ProseMirror + CodeMirror 6），
功能与行为保持一致；应用外壳则各用平台原生实现（原生窗口、菜单、对话框、文件系统集成），
并各自提供独立的发布产物（Windows 安装包 / macOS DMG）。

## 核心亮点

### 类印刷品渲染主题

编辑器内置 **四种文档渲染风格**：

- **默认风格（衬线）**——经典的 Markdown 渲染风格，采用衬线字体，在人文与现代间找到平衡。
- **默认风格（无衬线）** —— 适合屏幕阅读和日常编辑，是大多数编辑器较为主流的 Markdown 渲染风格，追求效率和清晰的体验。
- **印刷物（现代）** — 衬线字体（Times New Roman / 宋体），段落**两端对齐**、**首行缩进**，页面留白宽裕，模拟现代书籍电脑排版效果。适合长文写作和审阅。
- **印刷物（复古）** — 在现代印刷物布局基础上换用更具传统印刷质感的字体与字重，强化标题的黑体对比和引文的斜体表达，具有旧书铅字印刷的美感。

所有风格均支持一键导出为 **PDF** 或 **HTML**，可自定义纸张大小、页边距和页眉页脚。

### 所见即所得 Markdown 编辑

基于 **Tiptap/ProseMirror** 编辑器内核，支持完整的 CommonMark 和 GitHub Flavored Markdown 语法：

- 标题 1–6、段落、粗体、斜体、删除线、行内代码
- 超链接、图片（支持旋转和尺寸元数据）
- 有序/无序列表、任务列表、引用块
- 围栏代码块（含语言标识）、水平线
- GFM 表格（含行列插入/删除/对齐）
- LaTeX 数学公式（行内 `$...$` 与块级 `$$...$$`，KaTeX 渲染）

所有编辑操作均通过安全通信协议在原生宿主与编辑器之间同步，每条消息包含 `documentId` 和 `revision`，确保数据一致性。

### 工作区管理

支持打开文件夹作为工作区，异步加载文件树，提供树状视图和列表视图。自动监控外部文件变更，支持按名称/内容搜索文档，文档大纲与编辑器光标位置实时同步。

### 多窗口

支持打开多个彼此隔离的窗口实例，可通过「新建窗口」菜单创建，也可将文档在新窗口中打开。

### 源码模式

内置 CodeMirror 6 源码编辑模式，可在可视化编辑和 Markdown 源码之间即时切换；纯文本（.txt）文档固定使用源码模式。

### 查找替换

当前文档内全文查找和替换，支持大小写敏感和全词匹配，高亮匹配项并支持逐个导航；关闭查找面板时自动清除高亮。

## 技术架构

```text
Windows: C# + .NET 10 WinForms  → 主窗口、原生菜单、文件管理、工作区、大纲、DPI
macOS:   Swift + AppKit         → 主窗口、原生菜单、文件管理、工作区、大纲、深色模式
         │
         ├── 共享前端 src/EditorWeb（TypeScript）
         │   ├── Tiptap / ProseMirror → 所见即所得编辑、Markdown AST 与序列化
         │   └── CodeMirror 6         → Markdown 源码模式
         │
         └── 平台桥：WebView2（Windows）/ WKWebView（macOS）
             └── native-shim.js 提供一致的 window.chrome.webview 消息桥
```

编辑器资源完全本地加载。

## 构建与运行

### Windows

```powershell
# 构建前端
$env:PATH = '<node-bin-path>;' + $env:PATH
pnpm --dir .\src\EditorWeb install --frozen-lockfile
pnpm --dir .\src\EditorWeb build

# 构建并运行主应用
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\src\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# 一次性（构建前端 + 编译 + 打包 .app + 启动）
./script/build_and_run.sh

# 发布打包（.app / ZIP / 品牌 DMG / 校验和）
./script/release/package.sh
```

## 项目结构

Windows 与 macOS 两套原生外壳按职责一一对应，共享同一套编辑器前端：

| 职责 | Windows | macOS |
|---|---|---|
| 主窗口 / 外壳 | `src/MarkLeaf/UI/` | `macos/Sources/MarkLeaf/Views/` |
| 原生菜单 | `src/MarkLeaf/Native/NativeMenuService.cs` | `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift` |
| 编辑器宿主 / 协议桥 | `src/MarkLeaf/Editor/EditorHostController.cs` | `macos/Sources/MarkLeaf/Services/EditorSession.swift` |
| 设置 / 偏好 | `src/MarkLeaf/Services/` | `macos/Sources/MarkLeaf/Services/AppSettings.swift` |
| 工作区 / 大纲 | `src/MarkLeaf/Workspace/` | `macos/Sources/MarkLeaf/Services/Workspace*` + `Views/SidebarView.swift` |
| 命令路由 / 状态 | `src/MarkLeaf/Commands/` | `macos/Sources/MarkLeaf/Services/EditorSession+*` |

```text
src/
├── MarkLeaf/              # Windows 原生外壳（C# WinForms）
├── EditorWeb/             # 共享编辑器前端（Tiptap/ProseMirror + CodeMirror 6）
macos/
├── Sources/MarkLeaf/      # macOS 原生外壳（Swift AppKit）
├── script/                # 构建 / 发布脚本（build_and_run.sh、release/package.sh）
└── Changelog/             # 四语言更新日志
tests/
├── MarkLeaf.Tests/        # C# 单元测试
└── TestData/              # 测试用 Markdown 数据
```
