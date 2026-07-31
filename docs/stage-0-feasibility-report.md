# MarkLeaf 阶段 0：环境和可行性验证

检查日期：2026-07-30

## 已完成

- 初始化 Git 仓库，默认分支为 `main`。
- 验证 .NET 10 SDK 10.0.302、Windows Desktop Runtime 10.0.10、WebView2 Runtime
  150.0.4078.105、Visual Studio 2022、Node.js 24.14.0 和 pnpm 11.9.0。
- 建立 `.NET 10 WinForms + WebView2 + Tiptap/ProseMirror` 最小原型。
- 前端依赖全部锁定版本并生成 `pnpm-lock.yaml`，静态资源完全本地打包。
- 建立带 `documentId`、`revision`、`requestId` 和协议版本的窄 JSON 消息桥。
- 加载复杂 Markdown，支持可视化编辑、撤销/重做、表格、任务列表、代码块、
  大纲提取和 Markdown 快照导出。
- 完成 Markdown 语义往返、中文/emoji、中文斜体、任务列表 DOM、composition 事件和编辑历史自动测试。
- 完成真实 WinForms/WebView2 自动集成冒烟：加载、插入中文文本、撤销、重做、
  请求最新快照、导出后正常退出。
- 建立资源占位清单和阶段 0 第三方许可证清单。

## 主要文件

- `src/EditorWeb/src/editor.ts`：Tiptap/ProseMirror 编辑器配置。
- `src/EditorWeb/src/main.ts`：WebView2 消息桥、revision、composition 和命令处理。
- `src/EditorWeb/tests/roundtrip.test.ts`：五项往返、IME 事件和撤销历史测试。
- `src/MarkLeaf.Prototype/MainForm.cs`：WinForms/WebView2 原型窗口与集成冒烟。
- `src/MarkLeaf.Prototype/EditorProtocol.cs`：版本化 JSON 消息协议。
- `tests/TestData/complex.md`：复杂 Markdown 阶段样例。
- `docs/stage-0-roundtrip-report.md`：往返能力、规范化和风险结论。
- `src/MarkLeaf.Prototype/Resources/RESOURCE-PLACEHOLDERS.md`：后续图片和图标位置。

## 构建与测试

- 前端构建：通过；TypeScript 检查和 Vite 生产构建成功，58 个模块。
- .NET 构建：通过；0 个警告、0 个错误。
- 自动测试：7 项通过、0 项失败。
- 集成冒烟：通过；真实 WebView2 加载、插入、撤销、重做和快照导出成功。
- 可视检查：通过；1364x894 物理窗口截图中标准标题栏、工具栏、左右树、正文、
  任务列表、大纲和状态栏均正常，无空白页、错误覆盖层、缺失资源或内容重叠。

## 已知限制或风险

- 真实 Microsoft 拼音候选窗无法在当前隔离会话可靠自动化，必须人工检查。
- Setext 标题、列表标记、表格空格和空行会被序列化器规范化，当前仅保证语义保真。
- 未知扩展块与任意 HTML 的无损保留尚未解决，需要原始块或源码模式策略。
- ImageGen 概念图连续两次未产出文件，因此本阶段以项目指南作为视觉基准；无遮挡
  最终实现截图已通过 `view_image` 检查，但没有可进行成对比较的概念图。
- 内置 Browser 插件因受限 Node 运行时无法读取用户 AppData 而失败；改用真实
  WebView2 集成冒烟和 DWM 窗口截图验证。
- 原生 Win32 `HMENU` 属于阶段 2，本阶段没有提前实现主菜单。

## 人工检查反馈修复

- 允许浏览器为缺少原生斜体字形的中文宋体合成倾斜，中文斜体命令和视觉均生效。
- 任务列表按 Tiptap 的真实 DOM 结构布局，复选框与任务文字保持同一行。
- 清除表格单元格内部段落余白并缩小单元格内边距，表格行高更加紧凑。
- 正文混排字体调整为英文 `Times New Roman`、中文 `宋体-简`；代码字体保持不变。

## 请开发者检查

1. 运行原型，使用 Microsoft 拼音连续输入中文、全角标点和 emoji，检查是否重复、
   丢字或跳光标。
2. 在任务列表、表格和代码块中测试回车、退格、Tab、撤销和重做。
3. 点击“导出 Markdown”，确认导出文本符合可接受的语义规范化范围。
4. 检查当前三栏布局、正文宽度、16 px 正文和原生系统按钮是否适合作为后续基线。
5. 决定是否批准 Tiptap/ProseMirror 作为后续编辑器内核并进入阶段 1。

本阶段已停止，等待开发者检查和确认。未开始阶段 1。
