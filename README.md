# MarkLeaf

MarkLeaf 是一款**跨平台**的轻量化 Markdown 可视化编辑器，支持 **Windows 与 macOS**，
提供接近所见即所得的编辑体验。界面风格简约低干扰，同时保留各平台桌面应用的原生系统行为。

## 仓库结构

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows 原生应用（C# WinForms）
│   │   ├── MarkLeaf/             #   主程序（.NET 10 + WebView2）
│   │   └── setup/                #   WiX 安装器
│   └── macos/                    # macOS 原生应用（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   主程序
│       ├── Changelog/            #   产品更新日志（四语言）
│       └── script/               #   构建 / 发布脚本
├── packages/
│   ├── editor-web/               # 共享编辑器前端（Tiptap/ProseMirror + CodeMirror 6）
│   └── styles/                   # 共享排版 / 主题样式（打印风格，两平台共用）
├── MarkLeaf.slnx                 # Windows 解决方案
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共享应用图标
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 平台支持

| 平台 | 技术栈 | 代码目录 |
|---|---|---|
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS | Swift + AppKit + WKWebView | `apps/macos` |

两个平台共享同一套编辑器前端（`packages/editor-web`）与打印样式（`packages/styles`），
功能与行为保持一致；应用外壳则各用平台原生实现（原生窗口、菜单、对话框、文件系统集成）。

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
- GFM 表格（含行列插入/删除/对齐、表格标题）
- 图片（支持旋转、尺寸元数据与图片标题）
- LaTeX 数学公式（行内 `$...$` 与块级 `$$...$$`，KaTeX 渲染，块级公式支持编号）
- Mermaid 图表（```` ```mermaid ```` 围栏代码块，渲染流程图 / 时序图等，导出时内联为 SVG）

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
apps/windows（C# WinForms）        apps/macos（Swift AppKit）
  主窗口 / 菜单 / 工作区 / 导出       主窗口 / 菜单 / 工作区 / 导出
        │                                  │
        ├── packages/editor-web ───────────┤   共享前端（Tiptap + CodeMirror）
        ├── packages/styles ───────────────┤   共享打印样式
        └── WebView2 / WKWebView           ┘   native-shim.js 消息桥
```

编辑器资源完全本地加载。

## 构建与运行

### 共享前端（可选，应用构建脚本会自动执行）

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # 产物输出到 packages/editor-web/dist
pnpm --dir packages/editor-web test        # vitest 前端测试
```

### Windows

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# 一次性（构建前端 + 编译 + 打包 .app + 启动）
./apps/macos/script/build_and_run.sh

# 发布打包（.app / ZIP / 品牌 DMG / 校验和）
./apps/macos/script/release/package.sh
```
