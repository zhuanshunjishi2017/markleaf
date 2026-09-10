# MarkLeaf for VS Code

[English](./docs/README.en.md) | [日本語](./docs/README.ja.md) | [繁體中文](./docs/README.zh-TW.md)

在 VS Code 中阅读和可视化编辑 Markdown，复用 MarkLeaf 的 Tiptap/ProseMirror 编辑内核、KaTeX、Mermaid 和排版样式。扩展由 TypeScript 编写，运行时使用 VS Code 提供的 API 与 Webview，没有 Electron 依赖或独立桌面壳。

当前版本 **0.2.5** 提供 35 项设置和 67 项可配置格式操作，支持格式刷、表格、脚注、公式与图表、图片资源、查找替换、大纲和阅读偏好。导出与打印尚未接入。

工具栏菜单在点击外部、按 Escape、切换菜单或焦点离开插件时收起。公式与 Mermaid 源码面板使用适配明暗主题的不透明底色，始终展开在对应内容下方，随文档滚动移出视野，不会随视口空间上下跳转或固定在窗口底部。公式符号面板会根据可用空间调整布局。

项目与扩展 README 均提供四种语言；界面翻译范围见下方“排版和偏好”。

## 安装和打开

本地构建得到 `artifacts/markleaf-vscode-0.2.5.vsix` 后，在 VS Code 扩展菜单选择 **Install from VSIX…**，或从仓库根目录执行：

```bash
code --install-extension artifacts/markleaf-vscode-0.2.5.vsix
```

安装并启用扩展后，新打开的 `.md` 或 `.markdown` 文件默认进入 MarkLeaf 渲染视图，可直接阅读和可视化编辑。已打开的源码标签可以通过 **Reopen Editor With… → MarkLeaf** 或 **Ctrl/Cmd+Shift+V** 切换，也可以从资源管理器右键选择 **MarkLeaf: Open Markdown**。

升级 VSIX 后请先保存文档，再运行 **Developer: Reload Window（开发人员: 重新加载窗口）**，使当前窗口重新加载扩展及其配置声明。如果设置页仍只有旧选项，或提示“没有注册配置 markleaf.shortcuts”，也请重新加载整个窗口后再录入键位；仅重启扩展宿主可能留下旧的设置注册状态。

MarkLeaf 使用 VS Code 的默认自定义编辑器声明。若已为 Markdown 指定了其他默认编辑器，或安装了多个默认 Markdown 编辑器，可在 **Reopen Editor With… → Configure default editor for…** 中选择 **MarkLeaf**。需要恢复默认源码打开方式时，在同一位置选择 **Text Editor**；扩展遵循 VS Code 的编辑器关联设置。

### Git 差异视图

在 VS Code 1.120 及以上版本，`.md` 和 `.markdown` 的 Git 更改、暂存区及提交历史对比默认使用 VS Code 原生源码 diff，显示行号、增删高亮及差异导航。普通文件仍默认使用 MarkLeaf 阅读和编辑。

扩展通过 `workbench.diffEditorAssociations` 提供默认值，不写入用户设置；已有用户或工作区的 diff 关联优先。若曾为 Markdown 显式指定其他 diff 编辑器，可将该设置中的 `*.md`、`*.markdown` 设为 `default`。升级后先运行 **Developer: Reload Window**，再关闭并重新打开已有对比标签。较旧的 VS Code 不支持这项自动关联，可在对比标签中使用 **Reopen Editor With… → Text Editor**，或升级 VS Code。

## 编辑和阅读

| 功能组 | 入口和行为 |
| --- | --- |
| 文字与段落 | 工具栏和“格式…”支持粗体、斜体、下划线、删除线、高亮、行内代码、清除格式、H1–H6、标题升降级、段落前后插入、复制和删除、列表缩进，以及五种 GitHub 提示框。菜单支持按名称搜索。 |
| 格式刷与段落操作柄 | 选择已有格式的文字，点击格式刷，再拖选目标文字；应用一次后退出，Escape 取消。段落左侧操作柄打开当前段落菜单，操作柄位于可编辑内容外。 |
| 表格 | 指定行列数插入、增删行列、列对齐、设置或清除表格标题、删除表格。菜单依据光标位置显示适用操作。 |
| 公式与 Mermaid | 插入行内/独立公式、公式编号、转换类型、删除；公式与图表双击打开共享源码控件，保留数学输入辅助。支持 Mermaid 代码渲染、编辑和重新渲染。 |
| 脚注与元数据 | 插入脚注、重命名标签、回到引用、清除引用、删除脚注；显示或插入 YAML Front Matter。标签不能重复占用已有定义或引用。 |
| 代码 | 选择代码块语言、复制代码、退出代码块，支持语法高亮开关。 |
| 剪贴板 | “编辑”提供复制为 Markdown、纯文本或 HTML，以及粘贴纯文本。普通复制同时提供选区文本和 HTML；纯文本粘贴保留字面的 Markdown/HTML 标记。 |
| 查找替换 | 在渲染内容中查找，支持大小写、全词、上/下一处、单次和全部替换。阅读模式可查找，替换需要编辑模式。 |
| 大纲与阅读 | “视图”提供 H1–H6 大纲、专注当前段落、打字机滚动、缩放、排版与配色。状态栏显示字符数、选区字符数、当前块及位置，悬停可查看详细统计。 |

**阅读 / 编辑**按钮切换模式。打开文档、查找以及切换模式或显示设置不会触发 Markdown 回写。阅读模式保留复制、查找、链接和大纲导航，需要改动文档的操作仅在编辑模式可用。

打开格式菜单、图片选择器或输入框后，操作仍应用到打开时的选区。如果等待期间文档已被修改，会提示重新选择。若图片文件已经保存，但原选区过期，提示中会保留资源路径，方便从新位置插入。

链接在阅读模式直接单击，编辑模式按住 `Ctrl`（macOS 为 `Cmd`）单击，支持网页、本地文件和文内标题定位。

## 图片资源

- 工具栏“图片”从文件选择，可一次插入多张；“编辑 → 图片地址”插入相对路径或网络 URL。支持 PNG、JPEG、GIF、WebP、SVG、BMP 和 AVIF。
- 直接粘贴或拖入图片时，将图片保存到文档旁的 `assets` 目录，再插入 Markdown 引用。每张图片最多 16 MiB；未保存文档以第一个工作区文件夹为基准，没有工作区时须先保存文档。
- 文件选择默认复制到资源目录，可用 `markleaf.fileImageHandling` 改为引用原文件。资源目录、相对路径及 `./` 前缀均可配置，文件名自动避免覆盖已有资源。
- 选中图片后通过“编辑 → 当前图片操作”替换图片、编辑标题、旋转、调整宽度或另存图片；阅读模式仅提供图片另存为。
- 图片展示使用 VS Code 资源地址，写回 Markdown 的仍是原始路径。引用工作区外图片时按图片所在目录加载资源；远程文件通过 VS Code 文件系统 API 处理。

## 快捷键与文档同步

| 操作 | Windows / Linux | macOS |
| --- | --- | --- |
| 源码 / 渲染双向切换 | `Ctrl+Shift+V` | `Cmd+Shift+V` |
| 渲染内容查找 | `Ctrl+F` | `Cmd+F` |
| 渲染内容替换 | `Ctrl+H` | `Cmd+Alt+F` |
| 粘贴纯文本 | `Ctrl+Alt+V` | `Cmd+Alt+V` |
| 保存 | `Ctrl+S` | `Cmd+S` |
| 撤销 / 重做 | `Ctrl+Z` / `Ctrl+Shift+Z` | `Cmd+Z` / `Cmd+Shift+Z` |

查找栏用 Enter / Shift+Enter 定位下一处 / 上一处，Escape 关闭。Ctrl+滚轮可调整文档缩放。源码 / 渲染切换、查找、替换和纯文本粘贴可在 VS Code Keyboard Shortcuts 中搜索 MarkLeaf 后自定义；两条视图切换规则分别作用于源码与渲染，改键时应一起调整。渲染快捷键仅在 MarkLeaf 获得焦点时生效，纯文本粘贴不接管查找框和公式源码等输入框。标题、粗体、斜体等默认沿用共享编辑器已有键位，可在 MarkLeaf 快捷键设置中改绑。源码切换绑定在 Markdown 编辑场景中接管原生预览快捷键，原生预览命令仍可通过命令面板使用。

### 格式快捷键设置

打开 MarkLeaf 文档后，选择 **视图 → 快捷键…**，或在命令面板运行 **MarkLeaf: Configure Shortcuts / 快捷键设置**。也可从“排版、主题与设置”进入。录键面板提供所有格式菜单操作的搜索、录入、清除和恢复默认；点击“保存键位”后立即生效，格式菜单和工具栏提示同步更新。

行内公式、独立公式及图表默认不绑定。给“行内公式”录入一组键位后，在渲染编辑区的光标位置按键即可插入公式并打开原有公式源码与符号辅助。表格尺寸、脚注等需要额外参数的操作仍使用原有输入框；不再需要先展开格式菜单。

扩展设置页中的 **Markleaf: Shortcuts** 保存同一份配置，也可直接编辑设置 JSON，例如：

```json
"markleaf.shortcuts": {
  "insertMathInline": "Mod+Alt+M",
  "insertMathBlock": "Mod+Alt+Shift+M",
  "toggleBold": "Mod+Alt+B",
  "toggleHighlight": ""
}
```

- `Mod` 在 macOS 表示 Cmd，在 Windows/Linux 表示 Ctrl；也支持显式 `Ctrl`、`Cmd`、`Alt`、`Shift`。字母和数字使用键盘的物理键位，避免 macOS Option 生成符号影响匹配。
- 支持单组组合键（A–Z、0–9 或 F1–F24），本版不支持连续组合键或标点键。普通字母/数字须搭配 Ctrl、Cmd 或 Alt，避免占用正常输入。
- 未配置的操作沿用默认键位；空字符串取消绑定。改绑和取消后，原键位在 MarkLeaf 渲染编辑区不再触发该操作。“使用默认”需要再点击保存，不修改其他操作。
- 与其他 MarkLeaf 操作重复、或使用保存/撤销/源码切换等保留键位时，面板会说明原因并阻止保存。手动 JSON 中的无效或重复项会在面板标明，相关快捷键停用，不退回旧键制造已生效的假象。
- 保存范围显示在面板顶部：已有文件夹级配置优先，其次工作区，否则保存为用户设置；不将继承的其他键位复制到当前范围。修改设置不会回写 Markdown 文档。
- 自定义格式键仅作用于当前 MarkLeaf 渲染编辑区；阅读模式、查找框、公式/图表源码输入框与原生 Markdown 源码编辑器不触发这些格式操作。中文组合输入及 AltGraph 输入保持原有行为。

所有格式操作也注册为独立 VS Code 命令，例如 `markleaf.insertMathInline`、`markleaf.insertMathBlock` 和 `markleaf.setHeading1`。如需连续组合键等高级规则，可在 VS Code Keyboard Shortcuts 中配置，并将条件限制为 `activeWebviewPanelId == markleaf.editor && markleaf.focus == document`。同一操作应在 MarkLeaf 配置中清除旧键，以免保留两套入口。

MarkLeaf 菜单仅显示 `markleaf.shortcuts` 的生效配置。VS Code 公共扩展 API 不提供已解析的全局键位查询，因此无法同步显示用户另行在 `keybindings.json` 中设置的覆盖规则，也无法检查其他扩展和系统截获的全部组合键；这类冲突请使用 VS Code Keyboard Shortcuts 检查。

VS Code 的 `TextDocument` 是文档事实源，负责保存、未保存标记和编辑历史。“源码 / 并排”打开原生 Markdown 编辑器，共用同一份文档。连续输入按同步批次撤销，中文组合输入在提交后同步；底部“已同步到 VS Code”表示编辑已进入文本缓冲区，是否写入磁盘以 VS Code 的未保存标记为准。

Webview 同时只提交一次编辑，收到版本确认后再提交期间累积的新内容，自身编辑确认不重建编辑器。如果其他视图同时修改文件，过期编辑不会覆盖新版本，未同步内容保留在 MarkLeaf，并提供“将未同步内容打开为草稿”。草稿由 VS Code 打开后，MarkLeaf 重新加载文件当前内容；请在源码中合并草稿。

每个文件允许一个 MarkLeaf 可视化视图，可与原生源码并排。隐藏视图保留上下文以保护未完成输入。日常保存会等待文档同步；VS Code 退出时可能跳过保存参与者，因此关闭窗口前应确认同步完成，冲突内容应先打开为草稿。

## 排版和偏好

通过“视图 → 排版、主题与设置…”选择常用选项，或在 VS Code 设置中搜索 `@ext:markleaf.markleaf`。设置遵循已有的用户、工作区或工作区文件夹作用域。

| 设置组 | 主要设置 |
| --- | --- |
| 排版与配色 | `typography` 提供九种原有样式：sans、serif、print、print-double、latex、retro-print、minimal、magazine、notebook；`colorTheme` 默认跟随 VS Code，也可选择十九种原有配色。印刷类样式用于屏幕排版，本版本没有导出动作。 |
| 字体和宽度 | `fontSize` 默认 16、`fontFamily`、`lineHeight`、`maxWidth` 默认 820、`ignoreMaxWidth`、`zoom`；样式使用的字体须已安装在系统中。 |
| 代码和中西文 | `showCodeHighlight`、`sourceFontFamily` / `sourceFontSize`（公式/图表源码控件）、`cjkLanguage`、`cjkAutoSpacing`；视觉间距不会插入源码空格。 |
| 视图 | `defaultMode`、`showOutline`、`focusMode`、`typewriterMode`、`showStatusBar`、`showBlockHandle`、`autoHideScrollbars`、`ctrlWheelZoom`。 |
| Markdown 编辑 | `codeFence`、`emphasisMarker`、`bulletMarker`、`exitBlockOnEmptyEnter`、`useShiftEnterHardBreak`、强调转换及字面符号转义。序列化设置在实际编辑后应用。 |
| 图片 | `imageDirectory`、`fileImageHandling`、`useRelativeImagePaths`、`prefixImagePathsWithDot`。 |
| 格式快捷键 | `shortcuts`；与“视图 → 快捷键…”的录键面板共用配置，只作用于 VS Code 的 MarkLeaf 渲染编辑区。 |
| 自定义 CSS | `customCss` 指向文档相对路径或绝对路径的 CSS 文件；仅在可信工作区加载，修改文件后重新加载编辑器。 |

普通 Markdown 源码的字体、缩进和快捷键使用 VS Code 原生编辑器设置。文件/文件夹、最近文件、标签页、自动保存、恢复、编码、换行、窗口布局和扩展更新也使用 VS Code 本身的能力。共享公式和图表控件按 VS Code 语言选择已有翻译；本次新增 Webview 菜单使用中文，格式命令面板标题使用中文，其他命令保留英文。

## 格式和交付边界

仅打开和阅读不会改动源文件。实际可视化编辑经过 Markdown 解析和序列化，可能规范化列表标记、空行、强调或其他源码格式；保留文档的换行符类型和既有末尾换行，不承诺逐字节保真。编辑范围为共享内核支持的段落、标题、列表、任务、引用、代码、表格、图片、链接、YAML Front Matter、脚注、公式、Mermaid 和提示框。MDX、自定义插件语法或需要保留任意原始 HTML 的文档，请使用源码编辑器。

下划线兼容读取边界明确的 `++文字++`，内侧首尾不能是空白，外侧不能与英文字母、数字、下划线或其他加号粘连。`C++`、`C++17` 和普通递增运算符保留为文本。可视化编辑后的下划线保存为 `<u>…</u>`，避免与字面加号或单词内部选区产生歧义，并保留连续空格及其他行内格式。原生版和 VS Code 扩展使用相同规则。

当前交付目标是桌面 VS Code，浏览器版没有扩展入口。远程 URI 已接入 VS Code 文件系统与资源 API，但 Windows、Linux、SSH/WSL 及真实剪贴板、拖放、输入法和快捷键交互仍需在对应环境验收。构建、Vitest 和模拟宿主测试不代替实际安装后的界面验收。

PDF、HTML 文件、长图片导出和打印未接入 VS Code 扩展，原生版已有导出功能不受影响。原生功能与 VS Code 入口的逐组对应见 [功能对应说明](./docs/feature-parity.md)。

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

`packages/editor-web/src/vscode.ts` 是扩展前端入口，`apps/vscode/src/extension.ts` 是 VS Code 文档适配层。Windows/macOS 入口继续使用 `main.ts` 和原生宿主协议。扩展构建输出到 `apps/vscode/dist`，原生前端仍输出到 `packages/editor-web/dist`。
