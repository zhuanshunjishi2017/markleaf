# MarkLeaf

MarkLeaf 是一款 **Windows 原生**的轻量化 Markdown 可视化编辑器，以 Markdown 为持久化格式，提供接近所见即所得的编辑体验。编辑体验参考 Typora，界面风格简约低干扰，同时保留 Windows 桌面应用的完整系统行为。

## 核心亮点

### Windows 原生

真正的 Windows 桌面应用。C# + .NET 10 WinForms 构建，使用原生 Win32 `HMENU` 菜单（非 `MenuStrip`），完整支持高 DPI（PerMonitorV2）、多显示器和 Windows 11 视觉样式。应用外壳使用系统绘制和原生控件，启动快、内存低，行为和系统无缝一致。

### 类印刷品渲染主题

编辑器内置 **三种文档渲染风格**，可从格式菜单一键切换：

- **Sans（无衬线现代风格）** — 适合屏幕阅读和日常编辑。
- **Print（现代印刷风格）** — 衬线字体（Times New Roman / 宋体），段落**两端对齐**、**首行缩进**，页面留白宽裕，模拟书籍排版效果。适合长文写作和审阅。
- **Retro Print（复古印刷风格）** — 在 Print 布局基础上换用更具传统印刷质感的字体与字重，强化标题的黑体对比和引文的斜体表达，带来经典书籍的阅读氛围。

所有风格均支持一键导出为 **PDF** 或 **HTML**，可自定义纸张大小、页边距和页眉页脚。

### 所见即所得 Markdown 编辑

基于 **Tiptap/ProseMirror** 编辑器内核，支持完整的 CommonMark 和 GitHub Flavored Markdown 语法：

- 标题 1–6、段落、粗体、斜体、删除线、行内代码
- 超链接、图片（支持旋转和尺寸元数据）
- 有序/无序列表、任务列表、引用块
- 围栏代码块（含语言标识）、水平线
- GFM 表格（含行列插入/删除/对齐）

所有编辑操作均通过安全通信协议在 C# 宿主与 WebView2 编辑区之间同步，每条消息包含 `documentId` 和 `revision`，确保数据一致性。

### 工作区管理

支持打开文件夹作为工作区，异步加载文件树，提供树状视图和列表视图。自动监控外部文件变更，支持最近工作区快速访问。文档大纲与编辑器光标位置实时同步。

### 多窗口

支持打开多个彼此隔离的窗口实例，可通过「新建窗口」菜单创建，也可将文档在新窗口中打开。

### 源码模式

内置 CodeMirror 6 源码编辑模式，可在可视化编辑和 Markdown 源码之间即时切换。

### 查找替换

当前文档内全文查找和替换，支持大小写敏感和全词匹配，高亮匹配项并支持逐个导航。

## 技术架构

```text
C# + .NET 10 WinForms         → 主窗口、原生菜单、文件管理、工作区、大纲、DPI
    + Win32 HMENU (P/Invoke)  → 系统原生主菜单
    + WebView2                 → 编辑器宿主
    + TypeScript               → 编辑器前端逻辑
    + Tiptap / ProseMirror     → 所见即所得编辑、Markdown AST 与序列化
    + CodeMirror 6             → Markdown 源码模式
```

编辑器资源完全本地加载，不使用 CDN。

## 构建与运行

```powershell
# 构建前端
$env:PATH = '<node-bin-path>;' + $env:PATH
pnpm --dir .\src\EditorWeb install --frozen-lockfile
pnpm --dir .\src\EditorWeb build

# 构建并运行主应用
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\src\MarkLeaf.App\MarkLeaf.App.csproj
```

## 项目结构

```text
src/
├── MarkLeaf.App/          # C# 主应用 (WinForms)
│   ├── Commands/          # 命令路由与状态解析
│   ├── Editor/            # WebView2 宿主、通信协议、会话管理
│   ├── UI/                # 主窗口、控件、对话框
│   ├── Native/            # Win32 HMENU P/Invoke
│   ├── Services/          # 设置、日志、外部链接
│   └── Workspace/         # 工作区模型与服务
├── EditorWeb/             # TypeScript 前端 (Tiptap + CodeMirror)
│   └── src/
└── MarkLeaf.Prototype/    # 早期技术原型
tests/
├── MarkLeaf.Tests/        # C# 单元测试
└── TestData/              # 测试用 Markdown 数据
```

> 原型 `MarkLeaf.Prototype` 仅用于技术验证，开发时以 `MarkLeaf.App` 为正式应用目标。
