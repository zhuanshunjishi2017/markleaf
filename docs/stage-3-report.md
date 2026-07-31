# 阶段 3：WebView2 生命周期与通信桥

## 已完成

- 正式应用在主窗口显示后异步初始化 WebView2，生命周期为 `NotStarted`、`Initializing`、
  `LoadingPage`、`WaitingForEditorReady`、`Ready` 和 `Failed`。
- 修正应用入口的 STA 线程保持问题，避免 WebView2 初始化出现 `RPC_E_CHANGED_MODE`。
- 将 EditorWeb 的锁定依赖生产构建复制到正式应用输出目录，不依赖开发服务器或 CDN。
- 通过 `https://editor.local/` 虚拟主机加载本地静态资源，保持 `ZoomFactor = 1`。
- 加载期间显示原生占位；资源缺失、Runtime/初始化异常、页面失败、ready 超时和进程失败
  会进入原生失败界面并记录日志。
- 建立版本 1 JSON 协议，包含 `type`、`requestId`、`documentId`、`revision` 和 `payload`。
- 宿主和前端均使用类型白名单、payload 校验、非负 revision 和 1 MiB 消息大小限制。
- 请求快照使用 requestId 配对；外来 documentId、旧 revision 和不匹配响应均被拒绝。
- 实现 ready、loadDocument、documentLoaded、requestSnapshot、snapshot 和 command 的真实通信链路。
- 拦截离开 `editor.local` 的导航、新窗口和外部拖放导航；禁用浏览器上下文菜单、状态栏、
  DevTools 和浏览器快捷键。
- 捕获前端 `error`、未处理 Promise rejection 和 WebView2 进程失败，日志不包含文档全文。
- 正式命令路由已能把当前编辑器支持的格式命令发送给 WebView2；未实现的命令保持禁用。

## 人工检查反馈修复

- 修复正常启动只完成 ready、但没有发送初始 `loadDocument` 的会话同步缺陷。此前宿主和
  前端 documentId 不一致，段落菜单命令会被前端安全校验静默拒绝，后续更新也被宿主
  记录为 stale/foreign 消息。
- 正常启动现在先加载空 Markdown 文档，收到 `documentLoaded` 后才启用段落菜单，避免
  菜单看似可用但命令尚不能执行。
- 抽取可测试的前端命令执行器，并覆盖正文、六级标题、粗体、斜体、链接、引用、代码块、
  无序/有序/任务列表和表格。
- 新增原生插入链接对话框，仅接受 `http`、`https` 和 `mailto`，拒绝 `javascript:` 等
  危险协议。
- 真实 WebView2 回归中，`setHeading1` 将“段落命令”转换为 `# 段落命令`，revision 更新为
  1，日志中 stale/foreign 拒绝数为 0。

## 主要文件

- `src/MarkLeaf.App/Editor/EditorLifecycleState.cs`：生命周期状态。
- `src/MarkLeaf.App/Editor/EditorSession.cs`：文档 ID、revision 和请求关联。
- `src/MarkLeaf.App/Editor/EditorProtocol.cs`：宿主协议序列化与严格校验。
- `src/MarkLeaf.App/Editor/EditorHostController.cs`：WebView2 初始化、安全策略和通信处理。
- `src/MarkLeaf.App/UI/Controls/EditorLoadingView.cs`：加载、失败和重试界面。
- `src/EditorWeb/src/protocol.ts`：前端协议类型与校验。
- `src/EditorWeb/src/main.ts`：编辑器握手、消息处理和异常上报。

## 自动验证

- 前端测试：23 项通过、0 项失败。
- 宿主测试：31 项通过、0 项失败。
- 前端生产构建：通过；58 个模块，所有资源输出到本地 `dist`。
- .NET 构建：通过；3 个项目，0 个警告、0 个错误。
- 真实 WebView2 成功场景：约 641 ms 到达 ready；加载 Markdown、执行 appendText、请求快照
  均成功；requestId 匹配，revision 从 0 更新为 1，程序退出码为 0。
- 真实失败场景：指定缺失静态资源目录后稳定进入 `Failed`，状态报告和日志正确，退出码为 0。
- 单元测试覆盖：非法版本、未知类型、超限消息、错误 payload、外来文档、旧 revision、请求
  不匹配、失败重试状态清理和 Ready 后页面重载。

## 请开发者检查

1. 启动应用，确认原生加载占位短暂出现后切换到空白 Markdown 编辑区，状态栏显示“编辑器已就绪”。
2. 检查编辑区字体、留白和 Windows 缩放是否正常，WebView2 不应再次放大。
3. 尝试段落菜单中已启用的粗体、斜体、标题、引用、代码块、列表和表格命令。
4. 关闭应用后，将 `bin/Debug/net10.0-windows/EditorWeb` 临时改名再启动，检查原生失败提示和
   “重试”按钮；检查完成后恢复目录名。
5. 确认启动、正常编辑器显示和错误提示符合预期后，再批准进入阶段 4。

本阶段已停止，等待开发者人工检查和确认。未开始阶段 4。
