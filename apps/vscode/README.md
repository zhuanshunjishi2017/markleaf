# MarkLeaf for VS Code

在 VS Code 中阅读和可视化编辑 Markdown，复用 MarkLeaf 的 Tiptap/ProseMirror 编辑内核、KaTeX、Mermaid 和排版样式。扩展由 TypeScript 编写，运行时使用 VS Code 提供的 API 与 Webview；没有 Electron 依赖或独立桌面壳。

## 安装和打开

本地构建得到 `artifacts/markleaf-vscode-0.1.0.vsix` 后，在 VS Code 的扩展菜单选择 **Install from VSIX…**，或执行：

```bash
code --install-extension artifacts/markleaf-vscode-0.1.0.vsix
```

安装并启用扩展后，新打开的 `.md` 或 `.markdown` 文件默认进入 MarkLeaf 渲染视图，可直接阅读和可视化编辑。已打开的源码标签可以通过 **Reopen Editor With… → MarkLeaf** 或 **Ctrl/Cmd+Shift+V** 切换，也可以从资源管理器右键选择 **MarkLeaf: Open Markdown**。

MarkLeaf 使用 VS Code 的默认自定义编辑器声明。若已为 Markdown 指定了其他默认编辑器，或安装了多个默认 Markdown 编辑器，可在 **Reopen Editor With… → Configure default editor for…** 中选择 **MarkLeaf**。需要恢复默认源码打开方式时，在同一位置选择 **Text Editor**；扩展遵循 VS Code 的编辑器关联设置。

## 使用

- **阅读 / 编辑**：工具栏切换模式。打开文档和切换模式不会触发 Markdown 回写。
- **格式**：粗体、斜体、删除线、行内代码；“格式…”提供标题、列表、任务列表、引用、代码块、提示框、表格、公式和 Mermaid 插入，以及表格行列操作。
- **保存**：使用 VS Code 的保存命令、自动保存或 `Ctrl/Cmd+S`。底部“已同步到 VS Code”表示编辑已进入文本缓冲区；是否已写入磁盘以 VS Code 的未保存标记为准。
- **撤销 / 重做**：使用 `Ctrl/Cmd+Z`、`Ctrl/Cmd+Shift+Z`，或工具栏按钮，由 VS Code 文档历史管理。连续输入按提交批次撤销；中文组合输入在提交时同步。
- **源码 / 并排**：打开 VS Code 原生 Markdown 编辑器，共用同一份 `TextDocument`。源码修改会更新可视化视图。
- **快捷切换源码 / 渲染**：`Ctrl+Shift+V`（macOS 为 `Cmd+Shift+V`）在原生 Markdown 源码与 MarkLeaf 渲染视图间双向切换；切回源码前等待编辑同步。此绑定在 Markdown 编辑场景中接管原生预览快捷键，可在 VS Code 键盘快捷方式中调整 `MarkLeaf: Toggle Source / Rendered View`；原生预览命令仍可通过命令面板使用。
- **图片**：展示文档中已有的本地图片和 HTTP/HTTPS 图片。“图片”可以插入相对路径或 URL；展示用的 Webview 地址不会写回 Markdown。本地资源范围为文档所在目录和当前工作区。独立文件引用目录外的图片时，请打开包含它们的工作区。
- **链接**：阅读模式直接单击，编辑模式按住 `Ctrl`（macOS 为 `Cmd`）单击。支持网页、本地文件和文内标题定位。
- **查找**：使用 VS Code Webview 的查找功能（`Ctrl/Cmd+F`）。

公式、图表和代码复用已有编辑控件。界面颜色跟随 VS Code，字号和正文最大宽度可在设置中调整：

| 设置 | 默认值 | 含义 |
| --- | --- | --- |
| `markleaf.defaultMode` | `edit` | 新打开视图的初始模式：`edit` 或 `read` |
| `markleaf.fontSize` | `16` | 正文字号，10–32 px |
| `markleaf.maxWidth` | `820` | 正文最大宽度，320–1600 px |

## 文档同步与格式边界

VS Code 的 `TextDocument` 是文档事实源，负责保存、未保存标记和编辑历史。Webview 同时只提交一次编辑，收到版本确认后再提交期间累积的新内容；自身编辑确认不重建编辑器。若其他视图同时修改文件，过期编辑不会覆盖新版本，未同步内容会保留在 MarkLeaf，并提供“将未同步内容打开为草稿”。草稿由 VS Code 打开后，MarkLeaf 重新加载文件当前内容；请在源码中合并草稿。

仅打开和阅读不会改动源文件。实际可视化编辑会经过 Markdown 解析和序列化，可能规范化列表标记、空行、强调或其他源码格式；保留文档的换行符类型和既有末尾换行，不承诺逐字节保真。编辑范围为共享内核支持的段落、标题、列表、任务、引用、代码、表格、图片、链接、YAML front matter、脚注、公式、Mermaid 和提示框。MDX、自定义 Markdown 插件语法或需要保留任意原始 HTML 的文档，请使用源码编辑器。

首版允许每个文件一个 MarkLeaf 可视化视图，并可与原生源码并排。Webview 暂时保留隐藏视图的上下文，以保护未完成输入。日常保存会等待同步；VS Code 退出时可能跳过保存参与者，因此关闭窗口前应确认同步完成，冲突内容应先打开为草稿。

当前交付目标是桌面 VS Code。浏览器版没有扩展入口；远程 URI 使用 VS Code 资源 API 解析，但 Windows、Linux 和远程工作区需要分别运行验证。图片粘贴、拖入文件后的复制策略、PDF、打印和长图导出未纳入首版。

## 开发与构建

从仓库根目录执行，Node.js 22.12+，使用项目指定的 pnpm：

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir apps/vscode install --frozen-lockfile
pnpm build:vscode
pnpm test:editor-web
pnpm package:vscode
```

开发运行可使用已有 VS Code，无需安装额外桌面运行时：

```bash
code --new-window --extensionDevelopmentPath="$PWD/apps/vscode" path/to/document.md
```

`packages/editor-web/src/vscode.ts` 是扩展专用前端入口；`apps/vscode/src/extension.ts` 是 VS Code 文档适配层。原生 Windows/macOS 入口继续使用现有 `main.ts` 和原生宿主协议。扩展构建输出到 `apps/vscode/dist`，原生前端仍输出到 `packages/editor-web/dist`。
