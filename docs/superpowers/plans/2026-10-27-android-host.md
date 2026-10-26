# Android 宿主实现计划（第一版 M1-M5）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 MarkLeaf 构建 Android 薄宿主（Kotlin + Compose + WebView），复用 editor-web 内核，交付查看 + 轻编辑 + 导出的完整第一版。

**Architecture:** 单 Activity Compose 壳，每标签一个保活 WebView，EditorBridge 实现 protocol.ts JSON 消息协议（`window.postMessage` 注入 + `markleafHost` JS 接口回传），DocumentSource 抽象下 SAF 实现，document-kernel 在 WebView 内运行、编解码原语由 Kotlin 注入。

**Tech Stack:** Kotlin 2.x、Jetpack Compose（BOM 2024.09+ 稳定线）、minSdk 26、targetSdk 36、AGP 8.7+、kotlinx.serialization、DataStore Preferences、Robolectric 4.13+、JDK 21。

**Spec:** `docs/superpowers/specs/2026-10-27-android-host-design.md`

## Global Constraints

- minSdk = 26；targetSdk = 36；compileSdk = 36；Java/Kotlin 目标 = JVM 17 字节码（AGP 默认），工具链 JDK 21
- protocol.ts 是唯一协议真源；宿主消息模型必须与之双向兼容（契约测试钉死）
- 编辑器资产来自 `packages/editor-web/dist`（构建根仓后拷入），禁止手工修改 dist 内文件
- `hostPlatform: 'Android'` 加入 `resolveNativeCapabilities`；`usesThemedVisualSelection: true`
- `assets.local` 拦截 origin 与桌面端一致（Windows 用 `https://assets.example`、macOS `https://assets.local`、Android 用 `https://assets.local`）
- 中文注释；文件单责；一切 IO 在 Dispatchers.IO；WebView 调用在主线程
- 无网络权限（`INTERNET` 仅经 WebView 系统组件；应用自身不发网络请求）——manifest 不得声明 `android.permission.INTERNET` 之外的网络权限
- 每个任务：测试先行，测试失败→实现→测试通过→commit

## Review Focus

1. **SAF 持久化权限失效**（重启后 `SecurityException`）——标签应转只读快照并提示重新授权，不得崩溃。测试：Task 9 的 `saf permission revoked` 用例
2. **大文档主线程卡顿**——SAF 读取与编码探测必须在 IO 线程，`loadDocument` 组装在主线程但 >2MB 时先行 `loading` 状态。测试：Task 8 的 2MB 文档计时断言（<500ms 主线程占用）
3. **IME 合成期间快照/保存**——compositionActive 时 snapshot 必须等待或跳过（桌面端已知问题），测试：Task 11 的 `snapshot during composition` 用例
4. **WebView 进程崩溃恢复**——`onRenderProcessGone` 必须走恢复快照重载而非白屏。测试：Task 12 的 robolectric 回调断言
5. **stale 消息吞掉状态**——文档切换后 revision 重置；`Accept` 拒绝时打日志不静默。测试：Task 6 的 `revision reset on new document` 用例

---

### Task 1: Gradle 工程骨架与 CI

**Files:**
- Create: `apps/android/settings.gradle.kts`, `apps/android/build.gradle.kts`, `apps/android/gradle.properties`, `apps/android/gradle/wrapper/*`（wrapper jar 由 gradle 生成）
- Create: `apps/android/app/build.gradle.kts`, `apps/android/app/src/main/AndroidManifest.xml`
- Create: `apps/android/app/proguard-rules.pro`, `.gitignore`（android 内）
- Modify: `.github/workflows/`（新增 `android.yml`）

**Interfaces:**
- Produces: 可运行的 `./gradlew help`；`app` 模块空壳（`MainActivity` 显示 "MarkLeaf" 文本）；`assembleDebug` 产出 APK
- Produces: `local.properties` 约定（`sdk.dir`），`ANDROID_HOME` 环境变量兜底

- [ ] **Step 1:** 写 `settings.gradle.kts`（`pluginManagement` 用 google()/mavenCentral()）、root `build.gradle.kts`（AGP 8.7.x、Kotlin 2.0.x、compose 插件）、`gradle.properties`（`org.gradle.jvmargs=-Xmx4g`、`android.useAndroidX=true`、`kotlin.code.style=official`）
- [ ] **Step 2:** 用本机 gradle 9.x 生成 wrapper（`gradle wrapper --gradle-version 8.10.2`；AGP 8.7 兼容线），提交 wrapper 全部文件
- [ ] **Step 3:** 写 `app/build.gradle.kts`：minSdk 26 / target 36 / compile 36；依赖 compose-bom、activity-compose、kotlinx-serialization-json、datastore-preferences、webview 无需依赖；unitTests 含 Robolectric
- [ ] **Step 4:** `AndroidManifest.xml`：`INTERNET` 权限（WebView 更新通道需要，用于系统组件）、`MainActivity` exported、`ACTION_VIEW` intent-filter（`.md`/`text/markdown`）、`windowSoftInputMode="adjustResize"`
- [ ] **Step 5:** 空壳 `MainActivity`（Compose `Text("MarkLeaf")`）+ `res/values/strings.xml`（app_name）
- [ ] **Step 6:** 验证：`JAVA_HOME=<JDK21> ./gradlew help assembleDebug` 退出码 0；APK 存在
- [ ] **Step 7:** 写 `.github/workflows/android.yml`：ubuntu-latest、JDK 21（temurin）、`gradle assembleDebug test`；push 触发
- [ ] **Step 8:** Commit `feat(android): gradle skeleton with CI workflow`

### Task 2: editor-web 资产同步任务

**Files:**
- Create: `apps/android/app/sync-editor-assets.gradle.kts`
- Modify: `apps/android/app/build.gradle.kts`（apply 该脚本 + task 依赖）
- Create: `apps/android/app/src/main/assets/editor/.gitkeep`

**Interfaces:**
- Produces: `gradle syncEditorAssets` task——把 `packages/editor-web/dist/**` 拷入 `app/src/main/assets/editor/`，写 `asset-manifest.json`（相对路径→SHA-256）；dist 缺失时构建失败并提示先跑根仓 `pnpm build:editor-web`
- Consumes: 根仓 `packages/editor-web/dist`（已存在的构建产物）

- [ ] **Step 1:** 写同步 task（Gradle `Copy` + `doLast` 生成 manifest；`inputs.dir(rootDist)` 声明增量）；`preBuild.dependsOn(syncEditorAssets)`
- [ ] **Step 2:** 验证：在根仓跑 `pnpm build:editor-web`（代理），再 `./gradlew syncEditorAssets`——assets/editor/index.html 存在、manifest 包含 `assets/index-*.js` 条目
- [ ] **Step 3:** 验证 dist 缺失场景：临时改路径→ task 失败并输出提示 → 还原
- [ ] **Step 4:** Commit `feat(android): sync editor-web dist into APK assets`

### Task 3: 协议消息模型（Kotlin 侧 protocol）

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/bridge/Protocol.kt`
- Test: `apps/android/app/src/test/java/com/markleaf/app/bridge/ProtocolTest.kt`

**Interfaces:**
- Produces: `@Serializable sealed interface HostMessage`（`LoadDocument/Command/ApplyStyles/...` 全集）、`@Serializable sealed interface EditorMessage`（`Ready/DocumentLoaded/Snapshot/...` 全集）、`fun parseHostMessage(json: String): HostMessage?`、`fun encodeEditorMessage(m: EditorMessage): String`、`const val PROTOCOL_VERSION = 1`、`const val MAX_MESSAGE_BYTES = 16 * 1024 * 1024`
- 数据字段与 protocol.ts 逐一对齐（documentId: String、revision: Long、payload: JsonElement、requestId: String?）

- [ ] **Step 1:** 从 `packages/editor-web/src/protocol.ts` 抄出消息全集清单（type 联合逐项），写成测试样例常量（每种消息一条真实 JSON 样例，字段照前端实际发送）
- [ ] **Step 2:** 测试：对每条样例 `parseHostMessage` 返回正确子类型且字段往返一致；未知 type 返回 null；超 16MB 抛 `MessageTooLargeException`
- [ ] **Step 3:** 运行测试确认失败（类未定义）
- [ ] **Step 4:** 实现 `Protocol.kt`（kotlinx.serialization 多态 + JsonElement payload）
- [ ] **Step 5:** 测试通过；Commit `feat(android): protocol message models mirroring protocol.ts`

### Task 4: EditorSessionState（stale 过滤 + requestId）

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/bridge/EditorSessionState.kt`
- Test: `apps/android/app/src/test/java/com/markleaf/app/bridge/EditorSessionStateTest.kt`

**Interfaces:**
- Produces: `class EditorSessionState { var documentId: Guid; var confirmedRevision: Long; fun startDocument(id: Guid, revision: Long = 0); fun accept(m: EditorMessage): Boolean; fun registerRequest(responseType: String, requestId: String? = null): String; fun completeRequest(id: String?, type: String): Boolean }`
- 语义对齐 `apps/windows/MarkLeaf/Editor/EditorSession.cs:38`（`Accept`），含 Windows 端缺失的修正：`startDocument` 重置 revision

- [ ] **Step 1:** 测试：documentId 不匹配拒绝；revision 回退拒绝；同 revision 接受；`startDocument` 后旧 revision 消息**接受**（修正行为，与 C# 不同处加注释）；requestId 注册/完成/类型不匹配失败
- [ ] **Step 2:** 失败→实现→通过
- [ ] **Step 3:** Commit `feat(android): editor session state with stale filtering`

### Task 5: EditorBridge（WebView 消息桥）

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/bridge/EditorBridge.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/bridge/BridgeReceiver.kt`
- Test: `apps/android/app/src/test/java/com/markleaf/app/bridge/EditorBridgeTest.kt`（Robolectric）

**Interfaces:**
- Consumes: Task 3 消息模型、Task 4 会话状态
- Produces: `class EditorBridge(webView: WebView, session: EditorSessionState, onEditorMessage: (EditorMessage) -> Unit)`；`fun post(message: HostMessage)`；`fun attach()`（注册 JS 接口 + ready 等待）；内部：`postToEditor` = evaluateJavascript 包 `window.postMessage(JSON.parse('…'))`（单引号/反斜杠转义）；`BridgeReceiver` `@JavascriptInterface fun postMessage(json: String)` 解析后回调
- Produces: `onEditorMessage` 回调在主线程；`ready` 到达前缓存宿主消息（`pendingOutbound: ArrayDeque`），`ready` 后 flush（对齐桌面时序：ready → applyStyles → stylesApplied → loadDocument）

- [ ] **Step 1:** Robolectric 测试：attach 后模拟前端发 `ready` → 断言 applyStyles 与缓存的 loadDocument 依序 post（捕获 evaluateJavascript 调用序列）；`revision` 过滤：伪造旧 revision 的 `commandStateChanged` → 不回调 onEditorMessage；转义：含单引号/换行的 payload 往返无损
- [ ] **Step 2:** 失败→实现→通过
- [ ] **Step 3:** Commit `feat(android): editor bridge with ready handshake and stale filter`

### Task 6: native-capabilities Android 分支（前端侧）

**Files:**
- Modify: `packages/editor-web/src/native-capabilities.ts`（hostPlatform 联合类型加 `'Android'`；postMessage 分支；assetOrigin 不变）
- Modify: `packages/editor-web/src/protocol.ts`（HostMessage/EditorMessage 的 window.chrome 声明如需）
- Test: `packages/editor-core/tests/`（如已有 capabilities 测试则扩展；无则新增 `native-capabilities.test.ts`）

**Interfaces:**
- Produces: `resolveNativeCapabilities('Android')` → `usesThemedVisualSelection: true, primaryActivationModifier: 'ctrl', installsFrontendWheelHandler: false, imageCaptureMode: 'scroll'`
- Produces: Android 下 `window.chrome.webview.postMessage` = `markleafHost.postMessage(JSON.stringify(msg))`（macOS shim 同构，进 native-shim 注入路径或 main.ts 能力分支——与 Windows 的 chrome.webview 原生存在区分）
- 注意：此任务改动会触发 `pnpm build:editor-web` 重新出 dist；三个桌面端回归由既有测试覆盖（跑 editor-core + editor-web 测试套）

- [ ] **Step 1:** 前端测试：`resolveNativeCapabilities('Android')` 断言四项能力值
- [ ] **Step 2:** 实现 capability + Android postMessage 分支（在 native-shim.js 的 Android 版本由宿主注入，见 Task 7）
- [ ] **Step 3:** 跑 `pnpm --dir packages/editor-core test`（255+ 全绿）+ `pnpm --dir packages/editor-web test`
- [ ] **Step 4:** Commit `feat(editor-web): android host capabilities`

### Task 7: MainActivity + WebView 装载（M1 收口：只读渲染）

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/MainActivity.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/editor/EditorWebViewFactory.kt`
- Create: `apps/android/app/src/main/assets/editor/native-shim.js`（由同步 task 生成，Android 版）
- Modify: `apps/android/app/sync-editor-assets.gradle.kts`（生成 Android native-shim.js：chrome.webview polyfill，postMessage→markleafHost，hostPlatform='Android'）
- Test: `apps/android/app/src/test/java/com/markleaf/app/editor/EditorWebViewFactoryTest.kt`（Robolectric：webview 设置项断言）

**Interfaces:**
- Consumes: Task 5 EditorBridge、Task 2 assets
- Produces: `EditorWebViewFactory.create(context): WebView`——settings：`javaScriptEnabled`、`domStorageEnabled`、`allowFileAccess=false`、`mediaPlaybackRequiresUserGesture=false`、`mixedContent=NEVER`、textZoom=100；`WebViewClient.shouldInterceptRequest` 拦 `https://assets.local/`（M1 先返回 404，Task 10 实装图片）；`WebChromeClient.onConsoleMessage` → Log
- Produces: MainActivity：`AndroidBerry` 骨架——顶部标签条占位 + WebView 容器 + `loadUrl("file:///android_asset/editor/index.html")`；宿主在 `ready` 后发 `applyStyles`（空 CSS）与 `loadDocument`（内置欢迎文档 markdown）

- [ ] **Step 1:** Factory 测试（Robolectric）：settings 全项断言
- [ ] **Step 2:** 实现 Factory + shim 生成（gradle task 里写文件：与 macOS shim 同构，桥名换 markleafHost）
- [ ] **Step 3:** MainActivity 装配：bridge.attach → 收到 `documentLoaded` 后日志确认；硬编码欢迎文档（含表格/公式/mermaid/代码块）
- [ ] **Step 4:** `./gradlew assembleDebug` 通过；adb 装到设备（明天模拟器）人工验收渲染
- [ ] **Step 5:** Commit `feat(android): webview editor shell with welcome document`

### Task 8: DocumentSource 抽象 + SAF 实现

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/documents/DocumentSource.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/documents/SafDocumentSource.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/documents/DocumentCodings.kt`
- Test: `apps/android/app/src/test/java/com/markleaf/app/documents/SafDocumentSourceTest.kt`（Robolectric + 临时 DocumentsContract 假实现或 Robolectric SAF shadow）

**Interfaces:**
- Produces: spec §4.1 的 `DocumentSource`/`DocumentRef`（签名照抄 spec）；`SafDocumentSource(context, treeUri)` 全方法实现
- Produces: `DocumentCodings.detect(bytes): EncodingInfo`、`encode(text, encoding): ByteArray`——供 kernel 原语注入（BOM/UTF-8 严格/GBK 用 `Charset` 探测链）
- Produces: stable string：`content://...` 原样 + 权限持久化（`takePersistableUriPermission`）

- [ ] **Step 1:** 测试：stable string 往返；`detect` 对 UTF-8 无 BOM/UTF-8 BOM/GBK 中文/UTF-16LE BOM 样本正确；list/read/write 对 Robolectric ContentResolver 假数据的异常路径（SecurityException 传播）
- [ ] **Step 2:** 失败→实现→通过
- [ ] **Step 3:** Commit `feat(android): SAF document source with encoding detection`

### Task 9: 打开/保存/恢复流程（M2 编辑闭环）

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/documents/DocumentController.kt`
- Modify: `apps/android/app/src/main/java/com/markleaf/app/MainActivity.kt`（接入 OpenDocument/ CreateDocument launcher）
- Create: `apps/android/app/src/main/java/com/markleaf/app/data/RecoveryStore.kt`
- Test: `apps/android/app/src/test/java/com/markleaf/app/documents/DocumentControllerTest.kt`、`RecoveryStoreTest.kt`

**Interfaces:**
- Consumes: Task 8 DocumentSource、Task 5 Bridge
- Produces: `DocumentController(source, bridge)`：`open(uri)`（read→detect→kernel decode→loadDocument→确认 documentLoaded）、`save()`（requestSnapshot→snapshot→encode→write→markSaved）、`onPauseSnapshot()`、`conflictCheck(existingBytes, snapshot)`（快速哈希比对 → `SaveConflict` 枚举）
- Produces: `RecoveryStore(context)`：`writePending(documentId, uri, markdown)` / `consume(documentId): RecoveryEntry?` / `discard(documentId)`
- Compose：`OpenDocument` launcher（`text/markdown` + `.md` MIME）、保存用 `requestSnapshot` 流程

- [ ] **Step 1:** 测试：open 流程 mock bridge 断言 loadDocument 载荷（markdown/编码）；save 流程断言 write 调用与 markSaved；冲突检测（外部修改 → SaveConflict.External）；恢复写入/消费/丢弃
- [ ] **Step 2:** 失败→实现→通过（含 Review Focus #2：open 在 IO 线程、主线程仅 UI 调度）
- [ ] **Step 3:** Commit `feat(android): document open/save/recovery flow`

### Task 10: 图片资源拦截 + 工作区侧栏（M3 前半）

**Files:**
- Modify: `apps/android/app/src/main/java/com/markleaf/app/editor/EditorWebViewFactory.kt`（assets.local 实装）
- Create: `apps/android/app/src/main/java/com/markleaf/app/documents/AssetResolver.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/ui/SidebarPanel.kt`（工作区树/列表 + 大纲）
- Test: `apps/android/app/src/test/java/com/markleaf/app/documents/AssetResolverTest.kt`

**Interfaces:**
- Consumes: DocumentSource（列目录）、outlineChanged 消息（大纲数据）
- Produces: `AssetResolver(source, documentDir)`：`resolve(path): ByteArray?`——相对路径/绝对路径/`..` 归一化（禁止逃出授权根，路径归一化测试钉死）；shouldInterceptRequest 返回 `WebResourceResponse("image/*", null, stream)`
- Produces: SidebarPanel Compose：文件夹树展开/文档点击 → DocumentController.open；大纲列表 → `scrollToPosition` 命令

- [ ] **Step 1:** AssetResolver 测试：相对/绝对/`../` 越界拒绝（返回 null）
- [ ] **Step 2:** 实现 resolver + 拦截器；侧栏 Compose（树用 `MutableState` 递归项；大纲来自 `outlineChanged` 消息缓存）
- [ ] **Step 3:** Commit `feat(android): asset interception and workspace sidebar`

### Task 11: 导出管线（M4）

**Files:**
- Create: `apps/android/app/src/main/java/com/markleaf/app/export/ExportController.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/export/ExportDialog.kt`（Compose）
- Create: `apps/android/app/src/main/java/com/markleaf/app/export/PdfExporter.kt`
- Test: `apps/android/app/src/test/java/com/markleaf/app/export/ExportControllerTest.kt`

**Interfaces:**
- Consumes: bridge `exportSelection/exportDocument` 消息（前端 export-html.js 产自包含 HTML）；`PrintManager`/`PrintDocumentAdapter`
- Produces: `ExportController.exportHtml(markdown): String`（经 WebView export 分支）、`exportPdf(context, documentName, attributes)`（`webView.createPrintDocumentAdapter` + `PrintManager.print`，SAF 目标由 `CreateDocument` launcher 回传 uri）
- Produces: 导出对话框状态机（格式/文件名/纸张/边距），打印友好开关通过 `command` 让前端注入样式

- [ ] **Step 1:** 测试：HTML 导出回调闭环（mock bridge）；打印友好 CSS 注入参数；文件名非法字符清洗（`/ \ : * ? " < > |` → `-`，与 Windows saveName 同规则）
- [ ] **Step 2:** 失败→实现→通过（含 Review Focus #3：compositionActive 时 snapshot 延后——bridge 侧挂起直到 compositionend）
- [ ] **Step 3:** Commit `feat(android): export pipeline html/pdf`

### Task 12: 完整壳打磨（M3 后半 + M5）

**Files:**
- Modify: `MainActivity.kt`（标签条、返回键链、深色模式）
- Create: `apps/android/app/src/main/java/com/markleaf/app/ui/TabStrip.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/ui/StatusBar.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/ui/ContextMenuSheet.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/data/SettingsStore.kt`
- Create: `apps/android/app/src/main/java/com/markleaf/app/ui/L10n.kt`
- Modify: `EditorWebViewFactory.kt`（onRenderProcessGone 恢复）
- Test: `SettingsStoreTest.kt`、`TabStateTest.kt`

**Interfaces:**
- Produces: 标签栏（最多 6 活跃 WebView，LRU 关闭；低内存 `onTrimMemory` 只留当前）；状态栏（`editorStatusChanged` 驱动）；ContextMenuSheet（contextMenuRequested → DropdownMenu，坐标 WebView→屏幕换算含 textZoom/scrollY）；返回键链（菜单/抽屉 → undo → 关标签 → 退出）；SettingsStore（DataStore：主题/编码默认/新行风格/侧栏模式/最近文件）；L10n（assets locales/*.json 加载）；onRenderProcessGone → RecoveryStore 重载
- 深色模式：`isNightMode` → applyStyles 重发主题 CSS

- [ ] **Step 1:** SettingsStore 测试（DataStore 读写往返、默认值）；标签 LRU 测试（7 个打开→最旧关闭）
- [ ] **Step 2:** 实现 UI 组件与恢复链（Review Focus #4：crash 恢复测试断言 rebuild 调用）
- [ ] **Step 3:** `./gradlew test assembleDebug` 全绿
- [ ] **Step 4:** Commit `feat(android): tabs, status bar, context menu, settings, crash recovery`

### Task 13: 源码模式移动端适配（M5）

**Files:**
- Modify: 前端 `source-editor` 相关（`packages/editor-web/src/main.ts` 的 CodeMirror 配置按 capabilities 分支）
- Modify: `apps/android` 桥（`toggleSourceMode` 已透传；本任务补Android 特有调整）
- Test: `packages/editor-core/tests/source-editor.test.ts` 扩展

**Interfaces:**
- Produces: Android 下 CodeMirror：`spellcheck=false`、手势选择启用、`inputStyle: 'contenteditable'`（Android IME 必需）、字体缩放跟随系统
- 验收点（模拟器人工）：中文拼音合成不丢字、选区句柄出现、滚动不闪

- [ ] **Step 1:** 前端测试：Android capabilities 下 CodeMirror 配置分支断言
- [ ] **Step 2:** 实现分支；`pnpm build:editor-web` → gradle sync
- [ ] **Step 3:** Commit `feat(editor-web): android source mode IME adaptations`

### Task 14: 发布准备

**Files:**
- Create: `apps/android/app/release.gradle.kts`（签名配置占位：debug 签名出可分发 APK）
- Modify: `.github/workflows/android.yml`（release tag 时出 APK artifact）
- Create: `apps/android/README.md`（构建说明：根仓构建顺序、SDK 安装、签名替换）

**Interfaces:**
- Produces: `gradle assembleRelease`（debug 签名）可分发包；CI artifact 上传

- [ ] **Step 1:** README（从根仓构建到 APK 的完整命令链）
- [ ] **Step 2:** CI release job；本机 `assembleRelease` 产出 APK
- [ ] **Step 3:** Commit `feat(android): release packaging and docs`

---

## 执行方式

用户已授权自主推进（2026-10-27）。执行采用 **Native 模式**（本会话逐任务实施，每任务完成后独立审计），原因：任务间接口依赖强（Task 3→5→7→9 一条链），会话内连续实施可以保证接口一致性；每任务审计记录写入 commit message 的 `Reviewed-by: self-audit` 段。

## 审计协议（每任务完成后执行）

1. 重读本任务的 Interfaces/Steps → 对照实际代码逐一核对
2. `./gradlew test` 相关测试类通过
3. 检查 Global Constraints 违例（IO 线程/协议字段/资产不可手改）
4. 检查 Review Focus 中属本任务的条目已测试
5. 结论记录于 commit message；失败项回修后再审
