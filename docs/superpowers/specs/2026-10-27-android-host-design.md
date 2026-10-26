# MarkLeaf Android 宿主设计（第一版）

**日期**：2026-10-27
**状态**：设计定稿（用户授权下自审自批；逐节审批记录见文末附录 A）
**作者**：ZCode（brainstorming/writing-plans 流程，用户授权自主推进）

---

## 1. 目标与定位

MarkLeaf 跨平台 Markdown 编辑器扩展到 Android，作为**第四个薄宿主**。

**定位**：查看 + 轻编辑，**保留导出能力**（PDF/HTML 全链路）。

**第一版包含**：

- 可视化编辑模式（复用 editor-web 内核，与桌面端逐字节同源）
- 源码模式（CodeMirror，含移动端软键盘/选区/IME 适配）
- 导出（PDF / HTML）
- SAF 文档访问（单文件 + 授权文件夹）
- 多标签页、工作区侧栏（树/列表）、大纲侧栏、状态栏
- 设置、最近文件、会话恢复、深色模式
- APK 直发（官网/F-Droid 路线），不上 Play

**第一版不包含**：

- 远程文档源（SMB/WebDAV）——只预留 DocumentSource 接口
- Play Store 发布、AAB
- 外接键盘快捷键体系（系统快捷键层自然工作，专用的 ShortcutManager 不做）
- 桌面端的可选字体安装、QuickLook、更新检查（移动端无意义或后续立项）

**成功标准**：

1. 与桌面端相同的 Markdown 渲染保真度（表格/公式/mermaid/任务列表/脚注）
2. 轻编辑闭环完整：打开 → 编辑 → 保存回原文件 → 导出
3. 软键盘下的编辑体验可用：光标定位、选区、中文 IME 不丢字
4. 大文档（≥1MB）打开不卡主线程
5. 空进程重启后未保存内容可恢复

## 2. 总体架构（方案 A：Kotlin 薄宿主 + WebView 复用）

```
apps/android/
├── app/                          ← Android 应用模块
│   ├── ui/                       ← Compose 壳：标签栏、侧栏、状态栏、设置、导出对话框外壳
│   ├── bridge/                   ← EditorBridge：protocol.ts 的 Kotlin 实现
│   ├── documents/                ← DocumentSource 抽象 + SafDocumentSource
│   ├── export/                   ← 导出管线（PDF/HTML 落盘）
│   ├── kernel/                   ← DocumentKernelBridge（编解码原语注入）
│   └── data/                     ← DataStore 设置、最近文件、会话恢复
├── gradle/ 等                    ← 构建配置
packages/editor-web/src/
└── native-capabilities.ts        ← 扩展点：hostPlatform 增加 'Android'
```

```
┌─────────────────────────────────────────────┐
│  Compose 壳（标签栏/侧栏/状态栏/对话框）        │  Kotlin，对应 Windows WinForms 壳
├─────────────────────────────────────────────┤
│  EditorBridge（protocol.ts JSON 消息协议）     │  Kotlin，对应 Windows EditorHost
├─────────────────── WebView ─────────────────┤
│  editor-web main.ts（编辑器 UI + 内核）        │  与桌面端逐字节同源
└─────────────────────────────────────────────┘
         ↑ SAF 文档 / ↓ 导出文件         （Android 系统服务）
```

**决策**：

1. **单一 Activity + Compose 导航**。手机：标签栏在顶部、侧栏为抽屉。平板（sw≥600dp）：双栏常驻（侧栏 + 编辑区）。桌面端的"窗口"映射为"标签页"（对应 Windows DocumentTab）。
2. **每标签一个 WebView**：标签切换保活不销毁（滚动/选区/撤销栈天然保留）。上限 6 个活跃 WebView（与 Windows 端标签管理一致），LRU 关闭最旧标签。低内存回调时只保留当前标签。
3. **protocol.ts 是唯一接口规范**。`hostPlatform = 'Android'` 加入 `resolveNativeCapabilities`；`usesThemedVisualSelection = true`（Android WebView 是 Chromium，支持 CSS Custom Highlight API）。
4. **document-kernel 在 WebView 内运行**：编解码原语 `__markleafDocumentCodec` 由 Kotlin 通过 `addJavascriptInterface` 注入，decode/encode 用 `java.nio.charset`。
5. **技术栈**：Kotlin 2.x、Compose（BOM 最新 stable）、minSdk 26 / targetSdk 最新 stable、AGP 最新 stable。无网络层依赖（纯本地应用）。测试：JUnit + Robolectric（JVM 上测桥与策略）+ espresso（后续 UI 冒烟）。

## 3. EditorBridge（协议桥）

### 3.1 传输原语

- **宿主→前端**：`webView.evaluateJavascript("window.postMessage(JSON.parse('…'), '*')")`（与 macOS `EditorSession.swift:896`、Windows `ExecuteScriptAsync` 同构）。JSON 单引号转义在 Kotlin 侧封装为 `postToEditor(message: EditorMessage)`。
- **前端→宿主**：`addJavascriptInterface(receiver, "markleafHost")`。`native-capabilities.ts` 在 Android 上把 `window.chrome.webview.postMessage` 实现为 `markleafHost.postMessage(JSON.stringify(message))`（**字符串传输**，避免 Android JS 桥对对象序列化的兼容问题）。
- **图片资源**：WebViewClient.shouldInterceptRequest 拦截 `https://assets.local/`（沿用现有常量），从 SAF 文档目录解析图片（对应桌面 AssetSchemeHandler/CustomWebViewSchemeHandler）。

### 3.2 消息与生命周期

- 宿主→前端：`loadDocument` / `command` / `applyStyles` / `setDocumentType` / `requestSnapshot` / `markSaved` / `setReadOnly` / `toggleSourceMode` / `setEditorFocusMode` / `setBlockHandleVisible` / `setLanguage` / `setMarkdownEditingSettings` / `setAutoHideScrollbar` / `exportSelection` 等按 protocol.ts 全集实现。
- 前端→宿主：`ready` / `stylesApplied` / `documentLoaded` / `commandResult` / `dirtyChanged` / `snapshot` / `selectionChanged` / `commandStateChanged` / `contextMenuRequested` / `blockMenuRequested` / `openLink` / `dropFiles` / `pasteImage` / `outlineChanged` / `outlineSelectionChanged` / `requestSave` / `zoomWheel` / `editorStatusChanged` / `codeBlockLanguageRequested` / `codeFormatRequested` / `error`。
- **requestId 关联**：Kotlin 侧 `MutableMap<String, CompletableDeferred<JsonObject>>`，`registerRequest/completeRequest` 语义与 C# EditorSession 一致。
- **stale 过滤**：Kotlin `EditorSessionState(documentId, confirmedRevision)`，实现 `accept(message): Boolean`——与 C# `EditorSession.Accept` 相同（revision < confirmed 拒绝；documentId 不匹配拒绝）。**注意**：文档切换时必须重置 revision（Windows 端 ConfirmedRevision 不重置导致新文档消息被吞的 bug 不迁移）。
- **时序**：`ready` 之后才发 `applyStyles` → `stylesApplied` 回包后发 `loadDocument`。WebView `onResume` 后重发 `markSaved`/当前只读态；`onPause` 触发快照。
- **线程**：所有 evaluateJavascript 在主线程；SAF IO/编码探测在 Dispatchers.IO，结果 post 主线程。

### 3.3 命令分发

Kotlin `CommandRouter`：以 `EditorCommandBindings.cs` 等价表为蓝本，命令分两类：

- **透传命令**：直接 `postToEditor(command)`（undo/redo/格式化命令/插入命令/滚动/缩放……）
- **宿主命令**：本地处理（save/saveAs/export/print/open/new/closeTab/查找面板/设置……），必要完再透传

命令状态（`commandStateChanged.actions`）驱动 Compose 顶部工具栏与菜单的启用/勾选。

## 4. 文档源与文件访问

### 4.1 DocumentSource 抽象

```kotlin
interface DocumentSource {
    /** 列出目录下的 Markdown 文档（工作区树/列表用）。 */
    suspend fun listChildren(folder: DocumentRef): List<DocumentEntry>
    suspend fun read(ref: DocumentRef): DocumentBytes
    suspend fun write(ref: DocumentRef, bytes: ByteArray): Unit
    suspend fun rename(ref: DocumentRef, name: String): DocumentRef
    suspend fun delete(ref: DocumentRef): Unit
    suspend fun createDocument(folder: DocumentRef, name: String): DocumentRef
    suspend fun createFolder(folder: DocumentRef, name: String): DocumentRef
    /** 可持久化的引用（存最近文件/会话）。 */
    fun toStableString(ref: DocumentRef): String
    fun fromStableString(value: String): DocumentRef?
}

sealed interface DocumentRef {
    data class Saf(val uri: Uri) : DocumentRef   // tree/document URI
}
```

第一版唯一实现 `SafDocumentSource`（DocumentsContract）。远程源（SMB/WebDAV）未来实现同一接口，UI 层零改动。

### 4.2 编码与换行

- 编码探测：document-kernel `detectEncoding`（WebView 内），原语注入 Kotlin 实现 BOM/UTF-8 严格性检查/GBK 尝试，与桌面端 `DocumentEncodingPolicy` 行为对齐
- 写回：保存时按文档原编码写回（默认 UTF-8 无 BOM），换行符按 `DocumentNewLinePolicy` 等价逻辑保留
- 换行符/编码选择 UI 在保存时呈现（与桌面端"编码变更"对话框对应，简化为底部弹层）

### 4.3 未保存恢复

- `onPause`/标签关闭前/低内存：把未保存 markdown 写入 app 私有 `recovery/` 目录（JSON：documentId、SAF uri、markdown、时间戳）
- 启动时发现恢复文件 → 恢复对话框（对应桌面 RecoveryService），选择恢复或丢弃；正常保存后删除对应快照

## 5. 导出管线

### 5.1 HTML

- 复用内核 `@markleaf/editor-core/export`（export-html.js 已是独立分包）：WebView 内 `exportDocument` 生成自包含 HTML → SAF 创建文件写入
- 图片内联：assets.local 拦截器已在 WebView 内工作，export-html 的内联逻辑直接复用

### 5.2 PDF

- 走 **WebView `printAdapter`（`PdfPrintAdapter`）+ `PrintManager`**：与桌面端同思路（WebKit/Chromium 打印管线渲染分页），系统打印框架输出到 PDF（Android 的"另存为 PDF"打印机），用户选择目标 URI（SAF）
- 页边距/纸张：`PrintAttributes`（A4/Letter、彩色、边距取设置）
- 页眉页脚（`{page}/{pages}`）：第一版用 Android 打印框架的默认能力；若系统不支持自定义页脚，PDF 页眉/页脚功能降级为不可用并在 UI 明示（不静默缺失）——与桌面端 PdfHeader/FooterPreset 的差距在 spec 级明示
- 打印友好模式（浅色覆盖）：导出前通过命令让前端注入打印样式（复用桌面端 injectPageMargins 的 HTML 处理，在 Kotlin 侧拼接）

### 5.3 导出 UI

- Compose 实现导出对话框（格式切换/文件名/纸张/边距），保存目标用 SAF `CreateDocument` 协程回调

## 6. UI 壳与平台适配

- **标签栏**：手机上横向滚动标签条（对应 Windows DocumentTabBar）；拖拽重排在第一版不做
- **侧栏**：手机 = 模态抽屉（工作区树/列表、大纲两个 tab）；平板 = 常驻双栏
- **右键/长按菜单**：前端 `contextMenuRequested` → Compose `DropdownMenu`（锚定坐标换算 WebView→屏幕坐标，考虑缩放与滚动）；菜单布局沿用已对齐的规范（普通/表格/代码块/图片/公式/脚注/只读）
- **返回键**：BackDispatcher 链：关闭菜单/抽屉 → 撤销（编辑器内有历史时经 command undo）→ 关闭标签 → 退出确认
- **系统分享/打开**：`ACTION_VIEW`/`SEND` 接收 `.md` 文件打开（`onNewIntent` 导航到对应标签）
- **软键盘**：`WindowInsetsCompat` 处理编辑区 insets；WebView `isFocusableInTouchMode`；Compose 壳不与 WebView 抢焦点
- **深色模式**：`isNightMode` → `syncThemeModeClass`（复用现有 CSS 主题机制）
- **状态栏**：沿用桌面字段（字数/行列/编码/缩放），Compose 实现缩放按钮
- **本地化**：复用 zh-CN/en-US/ja-JP/zh-TW 四语言 JSON（打包进 assets，Kotlin 侧简易 L10n 加载器）

## 7. 数据流总览

```
打开文档:
  SAF uri → SafDocumentSource.read(bytes)
    → EditorKernelBridge.detectEncoding（WebView 内 kernel + Kotlin 原语）
    → markdown text → bridge.postToEditor(loadDocument)
    → 前端 documentLoaded → 宿主确认 revision

编辑:
  WebView 内 ProseMirror → revision 变化 → dirtyChanged → 标签脏标记
  （前端撤销栈自管；宿主不复制文档状态）

保存:
  用户点保存（或系统返回键触发 requestSave）
    → 宿主 requestSnapshot → 前端 snapshot(markdown)
    → EditorKernelBridge.encodeDocument(按原编码)
    → SafDocumentSource.write → markSaved

导出:
  导出对话框 → export 命令 → 前端 exportSelection/exportDocument
    → HTML: 自包含 HTML → SAF CreateDocument 写入
    → PDF: 打印管线 → PrintManager → SAF 写入
```

## 8. 错误处理

| 场景 | 处理 |
|---|---|
| SAF 权限被撤销（重启后持久化权限失效） | 捕获 SecurityException → 提示重新授权，标签转只读快照 |
| 文件被外部修改 | 保存前读文件 mtime/内容哈希比对 → 冲突对话框（覆盖/另存/放弃） |
| 大文档（>2MB） | 协议上限 16MB 内直接加载；编辑区性能依赖前端（桌面端已有大文档优化） |
| WebView 渲染进程崩溃 | `onRenderProcessGone` → 销毁该 WebView → 重建 → 用恢复快照重载（对齐桌面 RestartEditor） |
| 编码不可识别 | 保持 bytes 存疑提示（与桌面编码对话框一致，用户可强制选择） |
| 磁盘满/写失败 | 保存失败提示 + 自动恢复快照兜底 |
| 前端 JS 异常 | `onConsoleMessage` 转发到应用日志；`error` 消息 → 状态栏提示 |

## 9. 测试策略

1. **JVM 单元测试（Robolectric）**：EditorBridge 消息序列化契约（对 protocol.ts 样例 JSON 双向校验）、stale 过滤、requestId 关联、编码探测、DocumentSource stable string 往返、CommandRouter 分发表完整性（对 EditorCommandBindings.cs 枚举全集）
2. **内核契约测试**：复用 `packages/editor-core/tests`（跑在 Node，不改）；Android 端只做 smoke：打包出的 dist 完整性（资源 hash 清单）
3. **仪器测试（后续）**：WebView 装载 dist → ready → loadDocument → snapshot 回环
4. **手动/模拟器验收清单**（明早开始）：IME 中文输入、表格点选、导出 PDF/HTML、SAF 权限撤销恢复、进程被杀恢复、深色模式切换

## 10. 构建与发布

- **构建**：Gradle wrapper（仓库内，无全局依赖）；`editor-web dist` 由根仓构建产物经 Gradle task（`syncEditorAssets`）拷入 `app/src/main/assets/editor/`，构建前检查 dist 存在性与 manifest 哈希
- **签名**：debug keystore 入库（与桌面端 ad-hoc 签名同哲学），release keystore 本地生成不入库（CI 用 debug 签名构建测试包）
- **CI**：GitHub Actions 新增 `android.yml`：JDK 17 + Android SDK（runner 自带）→ `gradle assembleDebug testDebugUnitTest`；跑在每次 push
- **分发**：APK 挂 GitHub Release（沿用桌面端 release 流程）
- **minSdk 26 / targetSdk 最新 stable**；APK 直发不分架构（WebView 与内核都是系统组件）

## 11. 里程碑（对应实现计划的阶段）

1. **M1 骨架**：Gradle 工程 + Compose 空壳 + WebView 装载 dist + ready/applyStyles 握手 + loadDocument 显示渲染文档（只读）
2. **M2 编辑闭环**：命令桥 + 保存回写 SAF + 脏标记 + 快照恢复
3. **M3 完整壳**：多标签 + 侧栏（树/列表/大纲）+ 状态栏 + 设置 + 深色模式 + 本地化
4. **M4 导出**：HTML/PDF 管线 + 导出对话框
5. **M5 打磨**：源码模式移动端适配、恢复流程、错误处理全表、性能（大文档/内存）

---

## 附录 A：设计审批记录（用户授权自主审批）

用户 2026-10-27 明确授权："你先连续处理，然后自己审批每一步的设计、最终设计文档，以及实施计划，然后自行推进每一步的实施，尽量多做一些工作，每一步都要独立审计完成情况。"

| 节 | 审批结论 | 依据 |
|---|---|---|
| §1 目标定位 | 通过 | 用户四问四答明确（查看+轻编辑+导出/源码模式第一版/SAF/APK直发+minSdk26） |
| §2 总体架构 | 通过 | 用户在清醒状态下确认方案 A；与桌面端同构事实核对无误 |
| §3 协议桥 | 通过（自审） | 与 macOS EditorSession/Windows EditorHostController 逐原语对照；stale 过滤含 Windows 已知 bug 的修正说明 |
| §4 文档源 | 通过（自审） | DocumentSource 接口签名覆盖工作区/最近文件/会话恢复所需全部操作；编码行为与桌面 DocumentEncodingPolicy 对齐 |
| §5 导出 | 通过（自审） | PDF 页眉页脚降级为显式声明（不静默缺失），符合"不完整实现"的反面要求 |
| §6 UI 壳 | 通过（自审） | 全部条目有桌面端对应物或明确的 Android 映射；无 TBD |
| §7 数据流 | 通过（自审） | 五条主链路与 §3/§4/§5 的机制一致 |
| §8 错误处理 | 通过（自审） | 覆盖 SAF 撤权/外部修改/崩溃/磁盘满等移动端特有故障 |
| §9 测试 | 通过（自审） | JVM 可测项明确列出；模拟器验收清单供用户明日使用 |
| §10 构建 | 通过（自审） | dist 同步方式与三端打包模式同构；无 Play 依赖 |
| §11 里程碑 | 通过（自审） | M1-M5 每个都产出可运行软件，与 plan 阶段一一对应 |

## 附录 B：自审清单结论

- **占位符扫描**：无 TBD/TODO；PDF 页眉页脚限制为显式降级声明而非含糊
- **一致性**：hostPlatform='Android' 在 §2/§3 一致；DocumentSource 接口与 §4.3 恢复流程一致；里程碑与测试清单对应
- **范围**：单 spec 可承载（薄宿主复用厚内核，无独立子系统）；M1-M5 在一个实现计划内分阶段交付
- **歧义**：WebView 数量上限、标签保活策略、PDF 页脚限制均给出确定值或显式降级
