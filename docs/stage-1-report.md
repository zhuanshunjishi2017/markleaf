# 阶段 1：桌面项目骨架与 DPI

## 已完成

- 创建正式 `MarkLeaf.App` WinForms 项目与 `MarkLeaf.Tests` 测试项目，保留阶段 0
  原型作为编辑器内核回归工具。
- 配置 .NET 10、nullable、视觉样式、标准系统标题栏、WinForms 默认 Segoe UI 与
  `PerMonitorV2`。
- `app.manifest` 声明 PerMonitorV2、Windows 10/11 兼容、长路径和 Common Controls v6。
- 创建工作区、编辑区和大纲三栏基础布局，全部使用 Dock、SplitContainer 和 WinForms
  PerMonitorV2 自动 DPI 缩放，控件与字体不再重复手工缩放。
- 创建原生 WebView2 加载占位，正式 WebView2 控件保持隐藏，未提前实现阶段 3 生命周期。
- 实现 JSON 设置服务、原子临时文件替换、损坏配置回退和按 DPI 缩放的窗口恢复；
  schema 2 使用 96-DPI 逻辑单位保存尺寸，并自动迁移 schema 1 的物理像素设置。
- 保存并恢复窗口位置、尺寸、最大化状态、工作区宽度和大纲宽度；无效屏幕位置会
  回到主屏工作区。
- 实现按日文件日志，记录启动、停止、DPI 变化、设置保存和异常。
- 建立正式应用资源占位清单，未引入图片、图标或临时品牌素材。

## 主要文件

- `src/MarkLeaf.App/MarkLeaf.App.csproj`：正式 .NET 10 WinForms 项目配置。
- `src/MarkLeaf.App/app.manifest`：DPI、视觉样式和 Windows 兼容声明。
- `src/MarkLeaf.App/UI/MainForm.cs`：标准主窗口、三栏布局和窗口状态保存。
- `src/MarkLeaf.App/UI/Controls/EditorLoadingView.cs`：原生编辑器加载占位。
- `src/MarkLeaf.App/Services/Settings/JsonSettingsService.cs`：异步 JSON 设置服务。
- `src/MarkLeaf.App/Services/Settings/WindowPlacementCalculator.cs`：DPI 与屏幕位置归一化。
- `src/MarkLeaf.App/Services/Logging/FileLogger.cs`：基础文件日志。
- `tests/MarkLeaf.Tests/`：设置和窗口布局单元测试。
- `src/MarkLeaf.App/Resources/RESOURCE-PLACEHOLDERS.md`：后续资源位置。

## 构建与测试

- 前端构建：本阶段未修改编辑器前端；阶段 0 的已锁定构建保持可用。
- .NET 构建：通过；3 个项目，0 个警告、0 个错误。
- 自动测试：7 项通过、0 项失败。
- 设置集成：通过；隔离设置目录中两次真实启动均正常保存、恢复并退出，日志完整。
- 当前系统 175% 布局：通过；窗口报告 2240x1400、工作区 385、大纲 385，配置保持
  1280x800、220、220 的逻辑尺寸，连续两次启动无重复放大。
- 跨 DPI 计算：通过；单元测试覆盖逻辑尺寸单次换算、最小尺寸、旧配置迁移和屏幕外回中。

## 视觉与保真检查

- 已验收基准：`markleaf-task-final.png`，阶段 0 原型主界面。
- 最新实现截图：`markleaf-stage1-dpi96.png`、`markleaf-stage1-dpi192.png`。
- 截图方法：启动真实 WinForms 应用，使用 Windows `PrintWindow` 直接渲染指定窗口。
- Browser/IAB：因插件 Node 运行时无法读取用户 AppData（EPERM）而不可用；本阶段是
  WinForms 原生窗口，使用真实进程、窗口报告和 PrintWindow 作为降级验证。
- 已通过 `view_image` 同时检查已验收基准、100% 和 200% 最新截图。

具体比较点：

1. 保留标准 Windows 标题栏、窗口阴影和系统窗口按钮。
2. 保留左工作区、中编辑区、右大纲的三栏信息架构。
3. 使用纯白编辑表面、系统灰色栏头、细分隔线和系统选中颜色。
4. 系统控件文字、加载标题、说明、状态栏在 100% 和 200% 下均清晰。
5. 工作区与大纲宽度严格按 DPI 成比例缩放，中央区域无裁切。
6. 加载占位保持单一焦点，不使用卡片、渐变、图片或装饰性元素。

首屏可见文案差异是阶段内有意差异：阶段 0 的文档内容和编辑工具栏在正式应用中
暂由“正在准备编辑器…”占位替代；没有添加未经批准的营销文案、徽章或装饰文本。

## 已知限制或风险

- 阶段 1 不初始化 WebView2，不显示真实 Markdown 正文；阶段 3 才实现生命周期和通信桥。
- Win32 `HMENU` 和统一命令系统属于阶段 2，本阶段未提前实现。
- 已在真实 168 DPI（175%）环境检查系统非客户区和客户区缩放；仍需开发者在多显示器
  环境检查跨显示器拖动时的动态缩放。
- 当前未提供应用图标、工具栏图标或图片，位置已在资源占位清单中记录。

## 请开发者检查

1. 在当前显示器启动正式应用，检查标准标题栏、三栏比例和加载占位。
2. 调整窗口大小和两个分隔条，关闭后重启，确认位置、大小和侧栏宽度恢复。
3. 在至少另一档 Windows 缩放下启动，确认字体、状态栏和占位内容清晰。
4. 将窗口在不同 DPI 显示器之间拖动，检查缩放和可拖动分隔条。
5. 确认阶段 1 外观后，决定是否批准进入阶段 2 原生 HMENU 与统一命令系统。

本阶段已停止，等待开发者检查和确认。未开始阶段 2。
