# Windows 原生轻量化 Markdown 编辑器开发指南

## 1. 文档目的

本文档用于指导 Codex 等编程智能体分阶段开发一款 Windows 原生风格、轻量化、支持 Markdown 可视化编辑的桌面应用。

产品目标参考：

- 编辑体验参考 Typora：以 Markdown 为持久化格式，提供接近所见即所得的可视化编辑。
- 界面风格参考 XMind 8：现代、简约、低干扰、较多留白，同时保留 Windows 桌面应用的系统行为。
- 桌面外壳尽量使用 Windows 原生控件和系统绘制。
- 菜单必须使用真正的 Win32 `HMENU`，而不是 WinForms `MenuStrip`。
- 编辑器必须正确响应高 DPI、多显示器和 Windows 11 视觉样式。
- 用户数据安全优先于功能数量和视觉效果。

本文档既是设计规范，也是实施清单。智能体在开发时必须严格遵守文末的“阶段门与暂停协议”。

---

## 2. 最终技术路线

### 2.1 推荐方案

采用以下混合架构：

```text
C# + .NET 10 WinForms
+ Win32 HMENU（P/Invoke）
+ WebView2
+ TypeScript + Milkdown/ProseMirror
+ CommonMark + GitHub Flavored Markdown
+ WinForms/Win32 原生控件
+ 必要时使用 GDI+ 或 Direct2D 自绘特殊区域
```

职责边界：

```text
WinForms / Win32
├─ 主窗口、系统标题栏和窗口行为
├─ 原生菜单 HMENU
├─ 文件对话框、消息框和系统集成
├─ 文件树、大纲、状态栏和布局
├─ 文档、保存、恢复、设置和日志
└─ DPI、快捷键及应用生命周期

WebView2 编辑区
├─ Milkdown/ProseMirror 可视化编辑
├─ Markdown AST 与序列化
├─ 表格、列表、引用、代码块和任务列表
├─ 选区、光标、输入法和撤销/重做
├─ 查找替换和大纲提取
└─ 源码模式（后续使用 CodeMirror 6）
```

### 2.2 为什么选择该路线

WinForms 适合快速、稳定地实现 Windows 桌面外壳，并且本身建立在 `HWND` 体系之上，便于接入原生菜单和原生子控件。ProseMirror 系编辑器则已经解决了富文本编辑最困难的问题，包括选区、输入法、文档模型、事务和撤销历史。

不建议使用 `RichTextBox` 或 `RICHEDIT50W` 独立实现 Typora 式编辑器。RichEdit 适合 RTF，不适合维护 Markdown AST，也难以稳定处理 Markdown 表格、嵌套列表、标记显隐和语义往返。

### 2.3 明确不采用的路线

- 不使用 WinForms `MenuStrip` 代替主菜单。
- 不用 `RichTextBox` 从零实现 Markdown 所见即所得编辑。
- 不使用正则表达式解析 Markdown。
- 不用 `DataGridView` 模拟文档中的 Markdown 表格。
- 不用 GDI+ 从零实现文字布局、光标、选区和输入法。
- 不在第一版实现自定义标题栏。
- 不在第一版大量使用 Owner Draw 控件或菜单。
- 不从 CDN 加载编辑器脚本、样式、字体或图标。
- 不在第一版同时追求浅色、深色和完全自定义主题。

---

## 3. 产品范围

### 3.1 MVP 支持的 Markdown

- 标题 1 至 6
- 普通段落
- 粗体、斜体、删除线
- 行内代码
- 超链接
- 图片
- 有序列表和无序列表
- 任务列表
- 引用块
- 围栏代码块及语言标识
- 水平线
- GitHub Flavored Markdown 表格

### 3.2 MVP 暂不支持

- 多人协作和云同步
- 插件系统
- 数学公式
- Mermaid
- 脚注和复杂 Pandoc 扩展
- 任意可执行 HTML
- Excel 级表格功能
- 多窗口之间的实时同步编辑（允许打开多个彼此独立的窗口实例）
- 完整深色模式
- 自定义非客户区和标题栏

### 3.3 数据真实性原则

磁盘上的 Markdown 文本是最终持久化格式。编辑器内部的 ProseMirror 文档是当前编辑状态，不是第二份可独立修改的持久化真相。

必须遵守：

- 编辑期间，以 ProseMirror 文档为活动状态。
- C# 宿主只保存编辑器确认过的快照。
- 保存时主动请求对应 revision 的最新 Markdown。
- 每条通信消息必须包含 `documentId` 和 `revision`。
- 旧文档或旧 revision 的延迟消息不得覆盖当前文档。

---

## 4. 总体界面结构

```text
MainForm（保留标准 Windows 标题栏）
├─ Win32 HMENU
├─ 简洁工具栏（可选，优先少量高频命令）
├─ 主 SplitContainer
│  ├─ 左侧栏
│  │  ├─ 工作区文件树 TreeView
│  │  └─ 文档大纲 TreeView
│  └─ WebView2 编辑区域
└─ 状态区
   ├─ 字数
   ├─ 当前段落或光标信息
   ├─ 编码与换行符
   └─ 编辑模式
```

视觉原则：

- 保留标准系统标题栏、窗口阴影、贴靠布局和系统菜单。
- 主菜单使用系统绘制的 `HMENU`。
- 标准按钮使用 `FlatStyle.System` 和 `UseVisualStyleBackColor = true`。
- 尽量使用 `SystemColors`，避免硬编码大量颜色。
- 界面以浅色主题为 MVP 目标，并兼容高对比度模式。
- 编辑区域使用纯净背景、弱分隔线、充足留白和有限的强调色。
- 不为了“现代化”移除键盘焦点、系统边框和无障碍状态。

---

## 5. 项目与构建配置

### 5.1 建议项目结构

```text
MarkdownEditor/
├─ src/
│  ├─ MarkdownEditor.App/
│  │  ├─ App/
│  │  ├─ UI/
│  │  ├─ Editor/
│  │  ├─ Documents/
│  │  ├─ Settings/
│  │  ├─ Native/
│  │  ├─ Resources/
│  │  └─ Program.cs
│  └─ EditorWeb/
│     ├─ src/
│     ├─ public/
│     ├─ package.json
│     └─ dist/
├─ tests/
│  ├─ MarkdownEditor.Tests/
│  ├─ MarkdownRoundTripTests/
│  └─ TestData/
├─ docs/
├─ tools/
├─ THIRD-PARTY-NOTICES.md
└─ README.md
```

### 5.2 WinForms 项目配置

使用机器上已安装且受支持的 .NET 10 SDK。如果开发环境尚未具备 .NET 10，则先报告，不得私自切换目标框架；经开发者同意后可暂用 .NET 8 LTS。

核心 `.csproj` 配置：

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWindowsForms>true</UseWindowsForms>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  <ApplicationVisualStyles>true</ApplicationVisualStyles>
  <ApplicationDefaultFont>Segoe UI, 9pt</ApplicationDefaultFont>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <ApplicationIcon>Resources\App\App.ico</ApplicationIcon>
</PropertyGroup>
```

入口：

```csharp
[STAThread]
private static void Main()
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
```

### 5.3 DPI 清单

应用清单必须声明 `PerMonitorV2`，且不得再混用 `SetProcessDPIAware` 等旧 DPI API。项目属性、清单和运行时代码不得配置互相冲突的 DPI 模式。

### 5.4 前端资源发布

Web 编辑器必须生成静态 `dist`，并随桌面应用本地发布：

```xml
<ItemGroup>
  <Content Include="EditorWeb\dist\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

发布版本禁止依赖开发服务器或 CDN。

---

## 6. 原生控件与系统绘制

### 6.1 原生主菜单

通过 P/Invoke 使用：

- `CreateMenu`
- `CreatePopupMenu`
- `InsertMenuItemW` 或 `AppendMenuW`
- `SetMenu`
- `EnableMenuItem`
- `CheckMenuItem`
- `DrawMenuBar`
- `DestroyMenu`

主菜单结构：

```text
文件：新建、打开、打开文件夹、保存、另存为、导出、最近文件、退出
编辑：撤销、重做、剪切、复制、粘贴、查找、替换
段落：正文、标题、引用、代码块、列表、表格
视图：侧边栏、大纲、专注模式、源码模式
帮助：快捷键、关于
```

实现要求：

- 使用 `WM_COMMAND` 路由命令。
- 动态维护启用、禁用、勾选和单选状态。
- 处理 Form 句柄重建，避免重复创建或泄漏菜单句柄。
- 原生菜单以文字、快捷键、分隔线和系统勾选标记为主。
- 禁止为了图标或深色主题将主菜单改为 Owner Draw。
- 菜单上显示的快捷键文本不会自动注册快捷键，必须由统一命令系统处理。

### 6.2 标准 WinForms 控件

适用控件：

- `Button`
- `TextBox`
- `ComboBox`
- `CheckBox`
- `RadioButton`
- `TreeView`
- `ListView`

系统绘制按钮建议：

```csharp
new Button
{
    AutoSize = true,
    MinimumSize = new Size(88, 0),
    Padding = new Padding(12, 4, 12, 4),
    FlatStyle = FlatStyle.System,
    UseVisualStyleBackColor = true
};
```

避免：

- `FlatStyle.Flat`
- 硬编码按钮背景和边框
- `OwnerDraw`
- 固定像素字体
- 取消焦点提示

### 6.3 复杂自绘区域

- 普通图表、时间轴、小型画布：使用 WinForms `Control` + GDI+。
- 需要系统主题部件：使用 UxTheme 的 `DrawThemeBackground` 等 API。
- 大型高性能画布：使用 Direct2D/DirectWrite，并绑定到 WinForms 控件 `Handle`。
- Markdown 正文编辑区域不使用上述方案从零实现，必须由 WebView2 编辑内核承担。

---

## 7. DPI 与多显示器规范

### 7.1 WinForms 部分

- 全局使用 `PerMonitorV2`。
- 顶层窗体设置 `AutoScaleMode = AutoScaleMode.Dpi`。
- 使用 `Dock`、`Anchor`、`TableLayoutPanel` 和 `FlowLayoutPanel`。
- 避免大量固定 `Location`、`Width` 和 `Height`。
- 自绘控件使用 `DeviceDpi` 或 `LogicalToDeviceUnits`。
- DPI 变化时重建位图、字体或其他像素相关缓存。
- 不得对 WinForms 已自动缩放的控件尺寸再次乘 DPI 比例。

### 7.2 WebView2 部分

- 默认保持 `ZoomFactor = 1`。
- CSS 使用 CSS 像素，普通布局不要再次乘 `devicePixelRatio`。
- 编辑器字号调整属于应用阅读设置，不等于 Windows DPI。
- 跨显示器时验证浮动菜单、选区、拖放坐标和弹出层位置。

### 7.3 必测环境

- 1920x1080 @ 100%
- 2560x1440 @ 125%
- 2560x1440 @ 150%
- 3840x2160 @ 150%
- 3840x2160 @ 200%
- 两台显示器使用不同缩放比例，并来回拖动窗口

---

## 8. 编辑器内核

### 8.1 技术组成

- TypeScript
- Milkdown
- ProseMirror
- CommonMark + GFM 插件
- 代码高亮使用 Shiki 或 Prism，语言按需加载
- 后续源码模式使用 CodeMirror 6

必须锁定依赖版本，禁止无评估地自动升级编辑器依赖。

### 8.2 编辑样式

- 正文最大宽度约 760 至 900 CSS px。
- 正文 15 至 16 CSS px，行高 1.6 至 1.75。
- 使用系统无衬线字体栈；代码使用系统等宽字体栈。
- 标题和块级元素通过间距建立层次，避免厚重边框。
- 代码块使用轻微灰色背景。
- MVP 不要求完整复制 Typora 的 Markdown 标记智能显隐。

### 8.3 Markdown 往返保真

保真等级：

1. 语义保真：结构和呈现含义相同。
2. 语法保真：尽量保留用户原始写法。
3. 字符保真：字节级完全一致。

MVP 必须保证语义保真。未发生内容修改时，不要重新序列化并覆盖原文件。可视化编辑后允许将等价 Markdown 规范化，但不得静默删除未知内容。

编辑器不支持的扩展语法应：

- 作为不可破坏的原始块保留；或
- 引导用户切换源码模式处理。

### 8.4 中文输入法

必须重点测试微软拼音、常见第三方中文输入法、日文、韩文、全角标点和 emoji。

在 `compositionstart` 到 `compositionend` 期间禁止：

- 整体重载文档
- 强制修改选区
- 自动格式化当前节点
- 将宿主旧状态写回编辑器
- 触发高频全文序列化

---

## 9. 宿主与编辑器通信

只使用窄范围、版本化的 JSON 消息协议，优先通过 WebView2 `postMessage`，不暴露宽泛的 .NET Host Object。

消息基础结构：

```json
{
  "protocolVersion": 1,
  "type": "contentChanged",
  "requestId": "optional-id",
  "documentId": "document-guid",
  "revision": 42,
  "payload": {}
}
```

最低消息集合：

- `ready`
- `loadDocument`
- `documentLoaded`
- `dirtyChanged`
- `requestSnapshot`
- `snapshot`
- `selectionChanged`
- `command`
- `commandStateChanged`
- `outlineChanged`
- `requestSave`
- `openLink`
- `dropFiles`
- `pasteImage`
- `findResult`
- `error`

通信要求：

- 类型白名单和 payload 校验。
- 协议版本检查。
- 请求和响应使用 `requestId` 关联。
- 设置消息大小限制。
- 拒绝不匹配当前 `documentId` 的消息。
- 拒绝低于当前确认 revision 的旧消息。
- 文档切换时清理或取消旧请求。
- JavaScript 异常必须传到宿主日志，但不得包含用户全文。

性能要求：

- 每次输入只发送 dirty、选区或命令状态等轻量消息。
- 全文快照使用防抖，建议 300 至 1000 ms。
- 保存前必须主动请求最新完整快照。
- 崩溃恢复快照采用较低频率。
- 大纲单独传输，不随全文传输。

---

## 10. WebView2 生命周期与安全

### 10.1 初始化状态机

```text
NotStarted
→ Initializing
→ LoadingPage
→ WaitingForEditorReady
→ Ready
→ Failed
```

- `Ready` 前到达的打开文件、恢复草稿和命令必须排队。
- 主窗口应先显示，再异步初始化 WebView2。
- 编辑区域在加载时显示原生占位界面。
- 初始化失败时显示可操作的错误和重试入口。
- 处理 Runtime 缺失、初始化超时、页面失败和 WebView2 进程崩溃。

### 10.2 Runtime 策略

MVP 推荐 Evergreen WebView2 Runtime，以减小安装包。启动时检查 Runtime 是否存在，缺失时给出明确说明。若未来发现升级导致兼容性风险，再评估 Fixed Version Runtime。

### 10.3 本地资源加载

使用虚拟主机映射，例如 `https://editor.local/`，加载本地静态资源。禁止发布版使用开发服务器。

### 10.4 安全规则

- 所有 Markdown、HTML、SVG 和远程资源均视为不可信。
- 配置 Content Security Policy。
- 禁止脚本标签、事件处理器和 `javascript:` URL。
- 净化粘贴的 HTML。
- 限制导航、新窗口和允许的 URL 协议。
- 外部网页必须交给系统浏览器打开。
- 拦截拖放导致的页面导航。
- 限制 `file://`、UNC 路径和任意本地资源访问。
- 不执行代码块。
- SVG 必须净化或以安全方式栅格化。

---

## 11. 文档、保存与恢复

### 11.1 文档模型

每个文档至少维护：

```csharp
public sealed class Document
{
    public Guid Id { get; init; }
    public string? FilePath { get; set; }
    public string Markdown { get; set; } = "";
    public Encoding Encoding { get; set; } = Encoding.UTF8;
    public bool HasBom { get; set; }
    public string NewLine { get; set; } = Environment.NewLine;
    public bool IsDirty { get; set; }
    public bool IsReadOnly { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset? LastKnownWriteTime { get; set; }
}
```

### 11.2 文件格式处理

- 支持 UTF-8、UTF-8 BOM，并检测常见 UTF-16 文件。
- 尽量保留原 BOM 状态和 `CRLF`/`LF`。
- 处理空文件、只读文件、长路径、网络路径和无扩展名文件。
- 文件操作不得阻塞 UI 线程。

### 11.3 安全保存

禁止直接截断原文件后写入。保存流程必须是：

```text
请求并确认最新编辑器快照
→ 在目标文件同目录创建临时文件
→ 写入并 Flush
→ 原子替换目标文件
→ 成功后更新 revision、时间戳和 dirty 状态
→ 失败时保留编辑状态，并尽可能保留可恢复临时文件
```

保存前检查外部修改。若磁盘文件已变化，提供重新加载、比较、另存为或明确强制覆盖选项，禁止静默覆盖。

### 11.4 自动恢复

- 输入停止后防抖更新宿主内存快照。
- 每 15 至 30 秒写一次恢复文件，实际间隔应可配置。
- 正常关闭并成功保存后清理对应恢复记录。
- 启动时检测未完成恢复记录。
- 恢复界面提供恢复、丢弃和比较。
- 恢复数据写入应用数据目录，不直接覆盖原文件。

---

## 12. 文件、工作区与外部修改

MVP 文件功能：

- 新建、打开、保存、另存为
- 打开文件夹和文件树
- 最近文件与最近文件夹
- 拖放打开
- 工作区内新建、重命名和删除
- 外部修改检测

使用 Windows 文件对话框。筛选器至少包含 Markdown、文本和所有文件。

`FileSystemWatcher` 事件只能视为“需要重新检查”的信号，不可直接驱动重载。事件应防抖后重新检查文件存在性、大小、时间戳，必要时计算内容哈希。当前文档存在未保存修改时禁止自动重载。

删除操作应优先可恢复，用户图片和工作区文件不得因编辑器内部引用变化而自动永久删除。

---

## 13. 图片与资源文件

### 13.1 用户文档图片

默认采用：

```text
document.md
document.assets/
├─ image-20260730-143012.png
└─ diagram.svg
```

要求：

- 粘贴和拖入图片时复制到文档资源目录。
- Markdown 中使用相对路径。
- 未保存文档的图片先放入按 document GUID 隔离的草稿资源目录。
- 首次保存或另存为时迁移资源并更新引用。
- 处理文件名冲突和大图片异步复制。
- 删除 Markdown 引用时不立即删除磁盘图片。
- 提供显式“清理未引用资源”功能，并先展示确认列表。

### 13.2 应用资源

推荐结构：

```text
Resources/
├─ App/App.ico
├─ Toolbar/Light/
├─ Toolbar/Dark/          # 后续版本
├─ Bitmaps/
├─ ResourceManifest.json
└─ Licenses/Icons.txt
```

应用图标 `.ico` 建议包含 16、20、24、32、40、48、64、128、256 像素尺寸，并单独优化小尺寸表现。

### 13.3 文件树与系统图标

- 文件类型和文件夹图标通过 Windows Shell API 获取。
- 简单场景使用 `SHGetFileInfoW`。
- 通用系统图标可用 `SHGetStockIconInfo`。
- 按扩展名、尺寸和主题缓存图标。
- 从原生 `HICON` 创建托管图标时必须 Clone，然后调用 `DestroyIcon`。

### 13.4 工具栏图标

编辑命令使用一套许可清晰、风格统一的线性 SVG 图标。MVP 构建时预生成 16、20、24、32 像素 PNG，以获得最简单稳定的 WinForms 渲染。

对应 16 DIP 的推荐像素：


| Windows 缩放 | 图片尺寸  |
| ---------- | -----: |
| 100%       | 16 px |
| 125%       | 20 px |
| 150%       | 24 px |
| 200%       | 32 px |


`TreeView` 和 `ListView` 的 `ImageList` 必须按当前 DPI 创建，DPI 变化后重建。禁止把 16 px 位图直接放大到 32 px。

### 13.5 菜单图标

原生主菜单默认不添加图标，以保持系统绘制、DPI、高对比度和辅助功能的稳定性。

### 13.6 Web 编辑器资源

- 编辑器端可直接使用本地 SVG 和 CSS `currentColor`。
- 不使用在线字体或 CDN 图标。
- WinForms 与 Web 编辑器应尽量共享相同的图标视觉语言。
- 所有第三方图标必须记录来源、版本和许可证。

---

## 14. 统一命令系统

菜单、快捷键、工具栏和编辑器不得各自实现业务逻辑。定义统一命令枚举和路由器，例如：

```csharp
public enum EditorCommand
{
    NewDocument,
    OpenDocument,
    SaveDocument,
    SaveDocumentAs,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    ToggleBold,
    ToggleItalic,
    InsertLink,
    SetHeading1,
    InsertTable,
    Find,
    Replace,
    ToggleSourceMode
}
```

命令路由负责：

- 判断当前文档和编辑器状态。
- 判断命令由宿主还是 Web 编辑器执行。
- 更新菜单、工具栏的启用和选中状态。
- 处理焦点，确保 `Ctrl+Z` 不同时触发两套撤销。
- 将快捷键统一映射到命令。

快捷键至少包括：

```text
Ctrl+N       新建
Ctrl+O       打开
Ctrl+S       保存
Ctrl+Shift+S 另存为
Ctrl+Z       撤销
Ctrl+Y       重做
Ctrl+F       查找
Ctrl+H       替换
Ctrl+B       粗体
Ctrl+I       斜体
Ctrl+K       插入链接
Ctrl+1..6    标题级别
F11          专注模式
```

---

## 15. 大纲、查找、表格与源码模式

### 15.1 大纲

- 由编辑器 AST 提取标题，不在 C# 端用正则重复解析。
- 通过节流的 `outlineChanged` 消息传输。
- 宿主使用 `TreeView` 显示层级。
- 支持点击跳转和滚动位置同步。

### 15.2 查找替换

第一版在 ProseMirror 文档模型内实现当前文档查找、方向、大小写、全词、替换和全部替换。不得依赖 WebView2 自带浏览器查找 UI。

### 15.3 Markdown 表格

MVP 仅支持 GFM 表格：添加/删除行列、对齐、Tab 导航和基础粘贴。不支持合并单元格、多段落单元格或 Excel 级交互。

### 15.4 源码模式

后续使用 CodeMirror 6，和可视化编辑器共用 Markdown 文本。切换模式前必须同步快照；解析失败时保留原文并显示错误位置。不得用 WinForms `RichTextBox` 作为正式源码编辑器。

需要明确并测试可视化与源码模式切换时的撤销历史。MVP 可接受重建撤销历史，但行为必须一致且有文档说明。

---

## 16. 日志、隐私、许可证和发布

### 16.1 日志

至少记录：

- 应用、.NET、Windows、WebView2 和编辑器前端版本
- 启动阶段耗时
- DPI 和显示器基本信息
- 文件打开、保存和恢复失败
- Web 通信协议错误
- JavaScript 未处理异常
- WebView2 进程失败

日志不得默认记录文档全文、剪贴板内容、令牌或其他隐私数据。路径应支持脱敏。

### 16.2 第三方许可证

维护 `THIRD-PARTY-NOTICES.md`，记录每个依赖的名称、版本、来源、许可证、修改情况和是否随应用分发。特别检查：

- WebView2
- Milkdown 和 ProseMirror
- Markdown 插件
- CodeMirror
- 代码高亮库和主题
- 图标集
- 字体
- HTML/PDF 导出组件

### 16.3 发布

- MVP 优先发布 x64 framework-dependent 包。
- 后续根据用户环境评估 self-contained、arm64 和安装器。
- 发布前确认 Web 编辑器 `dist`、图标和许可证文件完整。
- 所有资源必须支持离线运行。
- 后续实现 `.md` 文件关联、命令行文件参数和“打开方式”。

---

## 17. 性能目标

- 主窗口尽量在进程启动后 1 秒内可见。
- 编辑器尽量在 2 秒内可输入。
- 1 MB Markdown 文件应正常编辑。
- 5 MB 文件允许提示性能风险，但不能无响应或丢失内容。
- 文件 I/O、文件树扫描、图片复制和导出不得阻塞 UI 线程。
- 大纲更新和快照生成必须节流或防抖。
- 每个窗口实例只初始化自己的单文档编辑器；创建多个窗口时不得阻塞已有窗口。

记录下列时间点用于回归：

```text
进程启动
主窗口可见
WebView2 初始化完成
编辑器 ready
文档可输入
```

---

## 18. 分阶段开发计划

每个阶段都是一个“大功能阶段”。必须完成本阶段的代码、编译、自动测试、必要的手工检查说明和阶段报告，然后暂停等待开发者确认。未经确认不得开始下一阶段。

### 阶段 0：环境和可行性验证

任务：

- 检查 .NET SDK、Node.js、npm 和 WebView2 开发条件。
- 建立最小 WinForms + WebView2 + Milkdown/ProseMirror 原型。
- 加载一组复杂 Markdown，并编辑后导出。
- 验证中文输入法、撤销/重做、列表、表格和代码块。
- 记录不能无损往返的语法。

完成条件：

- 原型可编译运行。
- 基础 Markdown 可加载、编辑和导出。
- 已形成往返能力报告和风险清单。

阶段验证：

- 编译桌面项目和前端项目。
- 执行基础 Markdown 往返测试。
- 手工测试中文输入法和撤销/重做。
- 暂停并等待开发者决定是否继续该编辑器内核。

### 阶段 1：桌面项目骨架与 DPI

任务：

- 创建正式解决方案和目录结构。
- 配置 .NET、nullable、视觉样式、manifest 和 PerMonitorV2。
- 创建标准标题栏的主窗体和基础布局。
- 添加 WebView2 加载占位区。
- 建立基础日志和配置服务。

完成条件：

- 应用能启动、关闭并恢复基本窗口状态。
- 100%、150%、200% 下布局清晰。

阶段验证：

- `dotnet build`。
- 单元测试。
- 在至少两个 DPI 档位运行检查。
- 暂停等待开发者检查窗口外观和缩放。

### 阶段 2：原生 HMENU 与统一命令系统

任务：

- 封装安全的 P/Invoke 菜单服务。
- 创建文件、编辑、段落、视图和帮助菜单。
- 实现 `WM_COMMAND`、动态状态、快捷键和命令路由。
- 处理句柄重建和菜单资源释放。

完成条件：

- 菜单由 Windows 原生绘制。
- Alt 访问键、快捷键和启用状态正确。
- 无菜单句柄泄漏。

阶段验证：

- 编译和自动测试命令路由。
- 手工检查菜单键盘导航和高 DPI。
- 暂停等待开发者检查原生菜单体验。

### 阶段 3：WebView2 生命周期与通信桥

任务：

- 实现 WebView2 初始化状态机和失败 UI。
- 配置本地虚拟主机映射。
- 实现版本化 JSON 协议、请求响应和 revision 校验。
- 拦截导航、新窗口、拖放导航和危险协议。
- 捕获 JavaScript 与进程失败。

完成条件：

- 编辑器能够稳定 ready。
- 宿主可加载文本、请求快照和执行命令。
- 旧 revision 消息不会覆盖当前状态。

阶段验证：

- 编译桌面和前端。
- 通信单元/集成测试。
- 模拟初始化失败、重载和旧消息。
- 暂停等待开发者检查启动和错误处理。

### 阶段 4：单文档打开、编辑与安全保存

任务：

- 实现文档模型、编码和换行检测。
- 实现新建、打开、保存和另存为。
- 实现最新快照请求和原子保存。
- 实现 dirty 状态、关闭确认和保存错误处理。
- 检测磁盘外部修改。

完成条件：

- 普通和异常保存均不会造成内容丢失。
- 未编辑文件不会被无意义地重新格式化。
- 外部修改不会被静默覆盖。

阶段验证：

- 编译和文档服务测试。
- 测试 UTF-8、BOM、CRLF/LF、只读和保存失败。
- 手工执行打开、编辑、保存和关闭流程。
- 暂停等待开发者检查文件安全行为。

### 阶段 5：Markdown 基础可视化编辑

任务：

- 完成 MVP Markdown 节点和命令。
- 实现粗体、斜体、链接、标题、列表、引用、代码块和水平线。
- 实现编辑器撤销/重做和命令状态同步。
- 完善中文 IME 和粘贴行为。

完成条件：

- 基础语法可稳定编辑和保存。
- 往返测试达到约定的语义保真标准。

阶段验证：

- 编译桌面与前端。
- 执行 Markdown 黄金文件往返测试。
- 手工测试 IME、跨块选区、复制粘贴和多步撤销。
- 暂停等待开发者检查核心编辑体验。

### 阶段 6：表格、任务列表与图片

任务：

- 实现 GFM 表格的基础编辑。
- 实现任务列表。
- 实现图片粘贴、拖入、草稿暂存和资源目录迁移。
- 实现图片路径和安全处理。

完成条件：

- 表格和任务列表可可靠往返。
- 图片在保存、另存为和恢复后引用有效。
- 删除文本不会自动永久删除图片。

阶段验证：

- 编译和自动测试。
- 测试各种表格边界和图片路径。
- 手工检查图片粘贴、拖放和另存为。
- 暂停等待开发者检查复杂内容编辑。

### 阶段 7：侧边栏、工作区和大纲

任务：

- 创建文件树和大纲 TreeView。
- 实现打开文件夹、最近项目、异步加载和基本文件操作。
- 实现 AST 大纲、点击跳转和滚动同步。
- 正确使用 Shell 文件图标和 DPI ImageList。

完成条件：

- 大工作区加载不阻塞 UI。
- 大纲与编辑区同步稳定。
- 文件图标在不同 DPI 下清晰。

阶段验证：

- 编译和相关服务测试。
- 测试大量文件、重复 watcher 事件和外部变化。
- 在多 DPI 显示器上检查树和图标。
- 暂停等待开发者检查工作区体验。

### 阶段 8：查找替换、源码模式和多窗口实例

任务：

- 实现 ProseMirror 查找替换。
- 集成 CodeMirror 6 源码模式。
- 定义模式切换和撤销行为。
- 支持打开多个独立的主窗口实例，每个窗口仍只承载一个活动文档。
- 为每个窗口隔离文档、编辑器会话、revision、文件监控、dirty 状态和保存流程。
- 支持从菜单或工作区在新窗口中打开文档，并正确处理窗口级关闭确认。
- 同一文件被多个窗口打开时不做实时同步，继续依靠外部修改检测阻止静默覆盖。

完成条件：

- 查找替换在复杂文档中准确。
- 模式切换不丢失内容。
- 多窗口实例之间的 revision、保存状态和 WebView2 生命周期相互隔离。
- 任一窗口初始化、关闭或保存失败时，不影响其他窗口继续编辑。

阶段验证：

- 编译和自动测试。
- 测试模式切换、解析失败、多窗口打开、独立保存、同文件冲突和逐窗口关闭确认。
- 暂停等待开发者检查多窗口工作流。

### 阶段 9：自动恢复、设置与会话恢复

任务：

- 实现恢复快照和启动恢复流程。
- 保存窗口位置、侧栏宽度、最近文件和基础偏好。
- 恢复前验证窗口仍位于有效显示器。
- 实现异常关闭测试入口。

完成条件：

- 异常退出后可以恢复未保存内容。
- 恢复功能不会自动覆盖原文件。

阶段验证：

- 编译和恢复服务测试。
- 人工终止进程后验证恢复。
- 暂停等待开发者检查恢复体验。

### 阶段 10：安全、性能与无障碍加固

任务：

- 完成 CSP、HTML/SVG 净化和协议白名单。
- 优化启动、快照、大纲、文件树和大文档性能。
- 检查键盘操作、焦点、高对比度和屏幕阅读器基础行为。
- 完善日志脱敏和错误提示。

完成条件：

- 不可信 Markdown 不能执行脚本或读取任意文件。
- 性能达到目标，常用功能可仅用键盘操作。

阶段验证：

- 编译、自动测试和安全样例测试。
- 测量启动及 1 MB/5 MB 文档性能。
- 手工检查高对比度和键盘导航。
- 暂停等待开发者批准发布准备。

### 阶段 11：打包与发布候选版

任务：

- 完成应用图标、版本信息和资源清单。
- 生成 `THIRD-PARTY-NOTICES.md`。
- 验证离线资源、WebView2 Runtime 检测和安装流程。
- 后续按批准范围加入文件关联和命令行参数。

完成条件：

- 干净机器上可安装、启动、编辑、保存和卸载。
- 安装包不遗漏前端资源和许可证。

阶段验证：

- Release 编译、完整自动测试和安装冒烟测试。
- 输出发布候选版检查报告。
- 暂停等待开发者最终验收，禁止自动发布。

---

## 19. 每阶段强制工作流程

智能体处理每个阶段时必须按以下顺序执行：

1. 阅读本指南、仓库 `AGENTS.md` 和与阶段相关的现有代码。
2. 检查工作树，辨别并保留开发者已有的未提交修改。
3. 给出本阶段简短实施计划和验收标准。
4. 只实现当前已批准阶段，不擅自提前开发后续大功能。
5. 为非平凡业务逻辑补充自动测试。
6. 构建前端资源。
7. 执行 `dotnet build`，必要时执行 Release 构建。
8. 执行本阶段相关的全部自动测试。
9. 进行可自动化的集成或 UI 冒烟测试。
10. 汇报变更、测试结果、已知风险和人工检查步骤。
11. 明确写出“本阶段已停止，等待开发者检查和确认”。
12. 结束当前任务，不得自行开始下一阶段。

如果构建或测试失败：

- 必须先尝试修复当前阶段引入的问题。
- 不得把失败状态描述为完成。
- 无法修复时报告准确错误、已尝试方法和阻塞条件，然后暂停。
- 不得通过删除测试、放宽断言或隐藏错误来制造通过结果。

---

## 20. 阶段报告模板

每个阶段完成后使用以下格式：

```markdown
## 阶段 N：名称

### 已完成
- ...

### 主要文件
- `绝对或仓库相对路径`：用途

### 构建与测试
- 前端构建：通过/失败；命令与摘要
- .NET 构建：通过/失败；命令与摘要
- 自动测试：通过数、失败数
- 手工冒烟：已执行项目及结果

### 已知限制或风险
- ...

### 请开发者检查
1. ...
2. ...

本阶段已停止，等待开发者检查和确认。未开始下一阶段。
```

只有开发者明确表示“继续”“批准本阶段”或指定下一阶段后，智能体才能继续。

---

## 21. 自动测试与质量闸门

### 21.1 Markdown 黄金测试

必须覆盖：

- 嵌套列表和空列表项
- 有序/无序列表混合
- 多段引用
- 带语言代码块
- 转义字符和中文标点
- emoji 和代理字符
- 表格对齐
- 链接标题和相对路径
- HTML 块和未知扩展块
- 连续空行和尾随空格换行
- 可视化/源码模式往返

每个样例至少测试：首次加载、未修改保存、局部修改保存、撤销后保存和多次模式切换。

### 21.2 文件测试

- UTF-8 有/无 BOM
- UTF-16 检测
- CRLF/LF
- 空文件和大文件
- 只读、长路径、网络路径
- 保存失败和磁盘外部修改
- 异常退出和恢复

### 21.3 编辑测试

- 中英文 IME
- 列表回车和退格
- 跨块选区
- 大量文本粘贴
- 表格键盘导航
- 图片粘贴与拖放
- 多步撤销/重做
- 快捷键与菜单状态

### 21.4 UI 与系统测试

- Alt 菜单访问键
- Tab 焦点顺序
- 100%、125%、150%、200% DPI
- 不同 DPI 显示器间移动
- 高对比度
- 键盘完整操作
- Windows 11 浅色主题
- WebView2 缺失或失败提示

### 21.5 发布质量闸门

发布候选版必须满足：

- 复杂 Markdown 语义往返测试通过。
- 中文 IME 不出现文字重复和光标跳转等已知严重问题。
- 保存失败不会丢失内容。
- 外部修改不会被静默覆盖。
- 崩溃后可以恢复草稿。
- 多 DPI 测试通过。
- 前端资源完全离线。
- 不可信内容不能执行脚本。
- 关键功能可使用键盘完成。
- 第三方许可证清单完整。

---

## 22. 关键风险清单

### 22.1 最高风险

1. Markdown 可视化编辑后破坏未知或复杂语法。
2. C# 与 JavaScript 两份状态相互覆盖。
3. 保存流程导致文件截断或静默覆盖外部修改。
4. IME 组合输入期间重载或修改选区。
5. 每次按键传输全文导致大文档卡顿。
6. WebView2 页面获得过宽的本地文件或宿主访问能力。

### 22.2 中等风险

- WebView2 初始化和进程失败处理不完整。
- 文件监控重复事件导致错误重载。
- WinForms 和 WebView2 双重缩放。
- 固定低分辨率图标在高 DPI 下模糊。
- 多窗口实例的 revision、文件监控或保存状态串线。
- 模式切换破坏撤销历史。

### 22.3 应推迟的高成本功能

- 自定义标题栏
- Owner Draw 主菜单
- 完整深色 Win32/WinForms 主题
- 多标签页文档界面
- Excel 级表格
- 插件系统
- 云同步和多人协作

---

## 23. 开发中的总原则

1. 先证明 Markdown 编辑与往返可靠，再完善桌面外壳。
2. 用户内容安全优先于格式统一、性能微优化和视觉效果。
3. 系统外壳保持原生，把复杂定制集中在编辑区。
4. 只保留一个活动真相，并用 document ID 和 revision 管理异步消息。
5. 所有文件写入采用可恢复流程，异常不得破坏原文件。
6. 所有外部内容视为不可信，所有宿主能力采用最小权限暴露。
7. 使用布局系统和 PerMonitorV2，不靠固定像素拼界面。
8. 不过早自绘系统已经能够正确绘制的控件。
9. 所有依赖和资源必须可离线发布，许可证必须可追溯。
10. 每完成一个大功能，必须编译、测试、汇报并暂停等待开发者确认。

按照本指南实施时，第一项工作应是“阶段 0：环境和可行性验证”，而不是直接搭建完整应用。



&nbsp;
