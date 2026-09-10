# VS Code 功能对应说明（0.2.7）

本表按当前共享编辑器、macOS 菜单和 Windows 宿主已有职责核对。编辑功能复用共享内核，文件和窗口管理由 VS Code 提供；导出与打印在 VS Code 中使用已安装 Chrome/Edge，HTML 生成复用共享内核。表中的接入情况指源码链路和入口，实际平台交互仍需安装验收。

| 原有功能组 | VS Code 对应入口 | 实现位置 / 边界 |
| --- | --- | --- |
| Markdown 阅读与编辑 | 默认 Markdown 自定义编辑器、“阅读 / 编辑” | `extension.ts` + `vscode.ts`，由 TextDocument 管理保存和历史。 |
| 源码切换与并排 | Ctrl/Cmd+Shift+V、“源码 / 并排” | VS Code 原生文本编辑器，文本源只有一份。 |
| Git 源码差异 | 源代码管理更改、暂存区、提交历史对比 | VS Code 1.120+ 通过 `workbench.diffEditorAssociations` 默认使用原生文本 diff；普通文件仍默认使用 MarkLeaf，用户和工作区显式配置优先。 |
| 文字、段落、列表、提示框 | 工具栏、“格式…”、命令面板、右键、段落操作柄 | `formatting.ts` + 共享 `executeEditorCommand`。异步菜单保留原选区，文档变化则拒绝过期操作。 |
| 格式刷、段落操作柄 | 格式刷按钮与段落左侧按钮 | 复用格式捕获和 DOM 选区应用逻辑，操作柄组件在原生和 VS Code 间共享。 |
| 表格 | 上下文格式菜单 | 插入尺寸、增删行列、列对齐、标题和整表删除。 |
| 公式、Mermaid、代码 | 上下文菜单、双击源码编辑、代码语言按钮 | 复用原有渲染、数学辅助、源码控件，编辑结果进入 VS Code 文档历史。 |
| 脚注、Front Matter | 上下文格式菜单、共享定义跳转 | 脚注插入、标签校验/重命名、引用处理，元数据源码编辑。 |
| 图片 | 文件选择、地址输入、粘贴、拖放、当前图片操作 | `images.ts` 使用 VS Code 文件系统；复制/引用策略、路径配置、标题、尺寸、旋转、替换与图片另存为。 |
| 剪贴板 | 普通复制/剪切/粘贴及“编辑”菜单 | 选区 Markdown/HTML 源码/纯文本复制；可视模式的普通文本和纯文本粘贴按 Windows 规则解析 Markdown，显示转换、降级原因和失败；源码编辑保留字面文本。 |
| 查找与替换 | Ctrl/Cmd+F、Ctrl+H / Cmd+Alt+F | 渲染文档查找栏使用共享位置匹配与替换，跨文字格式匹配，不跨内联公式等原子节点。源码查找使用 VS Code。 |
| 大纲 | “视图 → 大纲” | H1–H6、当前标题跟随和点击定位；工作区文件树使用 VS Code Explorer。 |
| 阅读和排版 | “视图”、偏好和扩展设置 | 默认极简，九种排版、十九种配色或跟随 VS Code，字体/宽度/行高、中西文间距、缩放、专注、打字机、状态栏、自动隐藏滚动条。 |
| Markdown 编辑偏好 | VS Code Settings 中的 MarkLeaf 项 | 围栏/强调/列表标记、转义和 Enter 行为复用共享设置。源码字体和缩进由 VS Code 原生编辑器配置。 |
| 新建、打开、保存、另存、最近文件、文件夹、搜索 | VS Code File / Explorer / Search / Quick Open | 使用 VS Code 工作区和文本文件服务，纯文本文件继续使用原生编辑器。MarkLeaf 不建立另一个文件数据库。 |
| 标签页、窗口、侧栏、极简/全屏 | VS Code 编辑器组、窗口布局、Zen Mode、全屏 | 采用宿主提供的布局操作，不复制原生独立应用的窗口管理器。 |
| 自动保存和未保存恢复 | VS Code Auto Save / Hot Exit，以及 MarkLeaf 冲突草稿 | 正常内容由 VS Code 管理；被拒绝的未同步内容先打开为草稿，避免丢失。 |
| 自定义格式快捷键 | “视图 → 快捷键…”、MarkLeaf 设置、独立格式命令 | VS Code 专用操作目录与配置、录键、校验及菜单提示。复用原有公式辅助和格式命令，不更改原生宿主快捷键配置。 |
| 编码、换行、快捷键、语言 | VS Code 状态栏、Keyboard Shortcuts、显示语言 | 共享控件使用原有翻译，新增菜单暂为中文，格式命令标题为中文，其他命令保留英文；原生应用的快捷键配置文件不迁移。 |
| 自定义主题、字体 | `markleaf.customCss`、已安装系统字体、VS Code 主题 | 原有 CSS 样式复用，不附带字体安装器或原生主题目录管理器。 |
| 欢迎、帮助、关于、更新 | MarkLeaf Help walkthrough、扩展详情及 VS Code 扩展管理 | `docs/welcome.md` 随 VSIX 交付；版本与安装更新由 VS Code 扩展管理。 |
| PDF、HTML、PNG/JPG、预览、打印 | “导出…”菜单、独立命令和上次设置 | `export-html.ts` 共享文档生成；`DocumentExports` 获取 TextDocument 快照与内嵌资源，`export-browser.ts` 使用 puppeteer-core 驱动临时 Chrome/Edge。HTML 保存不需浏览器；交互预览/打印限本地窗口。 |

## 验证口径

现有 Vitest/jsdom 用例覆盖共享编辑器，新增聚焦用例覆盖 VS Code 选区、同步、显示设置、格式刷、查找替换、脚注、图片路径/文件处理及剪贴板消息。宿主测试使用内存文件系统和对话框模拟，验证参数、结果和错误传播，不操作用户工作区中的图片文件。

VS Code 前端和扩展宿主分别编译，并生成 VSIX；共享原生前端单独构建。实际 VS Code 窗口中的安装、输入法、快捷键、图片拖放，以及 Windows/Linux/SSH/WSL 行为需要对应环境验收。原生 macOS 与 Windows 应用使用各自的构建流程；扩展测试不代替原生应用验收。新增导出测试核对快照、取消与资源失败；实际 Chrome 输出验证不替代跨平台和物理打印设备验收。
