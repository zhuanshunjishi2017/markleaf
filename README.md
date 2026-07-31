# MarkLeaf

MarkLeaf 是一个按阶段开发的 Windows 原生轻量化 Markdown 编辑器。当前正式应用
已完成阶段 6.5，正在推进阶段 7，用于验证以下技术组合：

- C# + .NET 10 WinForms
- Microsoft WebView2
- TypeScript + Tiptap/ProseMirror
- CommonMark/GFM Markdown 加载、可视化编辑和导出

正式功能范围、架构约束和阶段门参见
`Windows原生Markdown编辑器开发指南.md`。

## 正式应用（阶段 7 开发中）

正式应用已经具备 WebView2 生命周期、安全通信、单文档安全保存，以及基础 Markdown
可视化编辑、撤销/重做、命令状态同步、IME 保护和安全粘贴。运行：

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\src\MarkLeaf.App\MarkLeaf.App.csproj
```

阶段 6 已支持表格、任务列表和图片。图片使用本机绝对路径；剪贴板位图写入应用目录下的 `Cache/ClipboardImages`。阶段 6.5 已补全状态栏和编辑区原生右键菜单，阶段 7 开始实现工作区文件树与文档大纲。

## 阶段 0 原型运行

先构建前端：

```powershell
$env:PATH = 'C:\Users\Zhuan\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;C:\Users\Zhuan\.cache\codex-runtimes\codex-primary-runtime\dependencies\bin\fallback;' + $env:PATH
pnpm --dir .\src\EditorWeb install --frozen-lockfile
pnpm --dir .\src\EditorWeb test
pnpm --dir .\src\EditorWeb build
```

再构建并运行桌面原型：

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\src\MarkLeaf.Prototype\MarkLeaf.Prototype.csproj
```

## 当前边界

- `MarkLeaf.Prototype` 只是技术可行性原型，不是 MVP。
- 自动恢复、工作区、多文档和源码模式属于后续阶段；当前不可执行的菜单项会保持禁用。
- 图片、应用图标和工具栏图标暂时留空；占位位置记录在
`src/MarkLeaf.Prototype/Resources/RESOURCE-PLACEHOLDERS.md`。
- 发布资源完全本地加载，不使用 CDN。
