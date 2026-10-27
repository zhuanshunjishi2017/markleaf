@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app

import android.os.Bundle
import android.util.Log
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.clickable
import androidx.compose.material3.Button
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDrawerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.lifecycle.lifecycleScope
import com.markleaf.app.bridge.EditorBridge
import com.markleaf.app.bridge.EditorMessage
import com.markleaf.app.bridge.EditorSessionState
import com.markleaf.app.bridge.HostMessage
import com.markleaf.app.documents.AssetResolver
import com.markleaf.app.documents.DocumentController
import com.markleaf.app.documents.DocumentEntry
import com.markleaf.app.documents.DocumentRef
import com.markleaf.app.documents.RecoveryStore
import com.markleaf.app.documents.SafDocumentSource
import com.markleaf.app.documents.isMarkdownName
import com.markleaf.app.editor.EditorWebViewFactory
import com.markleaf.app.editor.StyleAssets
import com.markleaf.app.export.ExportController
import com.markleaf.app.export.ExportFormat
import com.markleaf.app.export.ExportDialog
import com.markleaf.app.export.ExportOptions
import com.markleaf.app.export.PdfExporter
import com.markleaf.app.ui.OutlineItem
import com.markleaf.app.ui.WorkspaceSidebar
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

class MainActivity : ComponentActivity() {

    private lateinit var webView: WebView
    private lateinit var bridge: EditorBridge
    private lateinit var session: EditorSessionState
    private lateinit var source: SafDocumentSource
    private lateinit var recoveryStore: RecoveryStore
    private var controller: DocumentController? = null
    private var workspaceRoot: DocumentRef.Saf? = null

    private val documentLoaded = mutableStateOf(false)
    private val dirty = mutableStateOf(false)
    private val editorStatus = mutableStateOf<kotlinx.serialization.json.JsonObject?>(null)
    private val showStatisticsDialog = mutableStateOf(false)
    private val editorSourceMode = mutableStateOf(false)
    private val zoomPercent = mutableStateOf(100)
    private val documentEncoding = mutableStateOf("UTF-8")
    private val documentNewline = mutableStateOf("LF")
    private val showEncodingDialog = mutableStateOf(false)
    private val showNewlineDialog = mutableStateOf(false)
    private val showZoomDialog = mutableStateOf(false)
    private var pinchAccumulator = 100.0
    private var lastPinchApplyAt = 0L
    private val sidebarEntries = mutableStateOf<List<DocumentEntry>>(emptyList())
    private val outline = mutableStateOf<List<OutlineItem>>(emptyList())
    private val activeTitle = mutableStateOf("未命名")
    private val showExport = mutableStateOf(false)
    private val showLanguageDialog = mutableStateOf<Pair<Long, String>?>(null)
    private val languageInput = mutableStateOf("")
    private val showContextMenu = mutableStateOf(false)
    private val contextMenuX = mutableStateOf(0f)
    private val contextMenuY = mutableStateOf(0f)
    private val editorActions = mutableStateOf<Map<String, Pair<Boolean, Boolean>>>(emptyMap())
    private var welcomeDocumentId = WELCOME_DOCUMENT_ID
    private var lastKnownMarkdown = ""
    private var pendingSnapshot: kotlinx.coroutines.CompletableDeferred<EditorMessage.Snapshot.SnapshotPayload>? = null
    private var pendingExportHtml: kotlinx.coroutines.CompletableDeferred<String>? = null
    private var rebuiltWebView: WebView? = null
    private lateinit var resolver: AssetResolver

    private val openTreeLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree(),
    ) { uri ->
        if (uri != null) {
SafDocumentSource.takePersistablePermission(this, uri)
            workspaceRoot = DocumentRef.Saf(uri.toString())
            refreshWorkspace()
        }
    }

    private val openFileLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument(),
    ) { uri ->
        if (uri != null) {
            openDocument(DocumentRef.Saf(uri.toString()))
        }
    }

    private val createHtmlLauncher = registerForActivityResult(
        ActivityResultContracts.CreateDocument("text/html"),
    ) { uri ->
        if (uri != null) {
            lifecycleScope.launch {
                val html = exportHtmlBlocking()
                contentResolver.openOutputStream(uri)?.use { output ->
                    output.write(html.toByteArray(Charsets.UTF_8))
                }
                showToast("HTML 已导出")
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // targetSdk 35+ 强制 edge-to-edge：内容绘制到系统栏下。浅色主题要求
        // 状态栏用深色图标；顶栏/底栏经 statusBarsPadding/navigationBarsPadding
        // 让出系统栏区域，Surface 背景延伸到栏下保持同色。
        enableEdgeToEdge()
        WindowCompat.getInsetsController(window, window.decorView).isAppearanceLightStatusBars = true
        WindowCompat.getInsetsController(window, window.decorView).isAppearanceLightNavigationBars = true

        resolver = AssetResolver { path: String ->
            val active = controller?.activeRef
            if (active == null) {
                null
            } else {
                val documentDir = documentDirectoryOf(active)
                runBlocking { source.readSiblingBytes(documentDir, path) }
            }
        }
        webView = EditorWebViewFactory.create(
            this,
            assetResolver = resolver,
            documentDir = this::activeDocumentDirectory,
            onRenderProcessGone = this::handleRenderProcessGone,
        )
        session = EditorSessionState().apply { startDocument(WELCOME_DOCUMENT_ID) }
        source = SafDocumentSource(this)
        recoveryStore = RecoveryStore(this)
        bridge = EditorBridge(
            postScript = { script -> webView.evaluateJavascript(script, null) },
            session = session,
        )
        bridge.attach(webView)

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    val drawerState = rememberDrawerState(DrawerValue.Closed)
                    val scope = rememberCoroutineScope()
                    ModalNavigationDrawer(
                        drawerState = drawerState,
                        drawerContent = {
                            WorkspaceSidebar(
                                entries = sidebarEntries.value,
                                outline = outline.value,
                                onOpenDocument = { entry ->
                                    scope.launch { drawerState.close() }
                                    (entry.ref as? DocumentRef.Saf)?.let(::openDocument)
                                },
                                onOutlineClick = { item ->
                                    scope.launch { drawerState.close() }
                                    bridge.post(command("scrollToPosition", item.position.toString()))
                                },
                            )
                        },
                    ) {
                        Column(
                            modifier = Modifier
                                .fillMaxSize()
                                // 键盘展开时整体收起到底栏上方：状态栏与编辑区可见
                                .imePadding(),
                        ) {
                            TabStrip(
                                active = activeTitle.value,
                                dirty = dirty.value,
                                documentLoaded = documentLoaded.value,
                                onOpenFolder = { openTreeLauncher.launch(null) },
                                onOpenFile = { openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) },
                                onSave = { lifecycleScope.launch { saveActiveDocument() } },
                                onExport = { showExport.value = true },
                                onOpenDrawer = { scope.launch { drawerState.open() } },
                            )
                            EditorContainer(
                                webView = webView,
                                onPinch = ::onPinchZoom,
                            )
                            StatusBarPlaceholder(
                                status = editorStatus.value,
                                encoding = documentEncoding.value,
                                newline = documentNewline.value,
                                zoom = zoomPercent.value,
                                sourceMode = editorSourceMode.value,
                                onShowStatistics = { showStatisticsDialog.value = true },
                                onEncoding = { showEncodingDialog.value = true },
                                onNewline = { showNewlineDialog.value = true },
                                onZoom = { showZoomDialog.value = true },
                                onToggleMode = {
                                    bridge.post(command("toggleSourceMode", ""))
                                    editorSourceMode.value = !editorSourceMode.value
                                },
                            )
                            if (showStatisticsDialog.value) {
                                DocumentStatisticsDialog(
                                    status = editorStatus.value,
                                    encoding = documentEncoding.value,
                                    newline = documentNewline.value,
                                    onDismiss = { showStatisticsDialog.value = false },
                                )
                            }
                            if (showEncodingDialog.value) {
                                EncodingDialog(
                                    current = documentEncoding.value,
                                    onSelect = { info ->
                                        controller?.setEncoding(info)
                                        documentEncoding.value = info.let {
                                            if (it.hasBom && it.name == "UTF-8") "UTF-8 BOM" else it.name
                                        }
                                        showToast("保存时将使用 ${documentEncoding.value} 编码")
                                        showEncodingDialog.value = false
                                    },
                                    onDismiss = { showEncodingDialog.value = false },
                                )
                            }
                            if (showNewlineDialog.value) {
                                NewlineDialog(
                                    current = documentNewline.value,
                                    onSelect = { style ->
                                        controller?.setNewline(style)
                                        documentNewline.value = style
                                        showNewlineDialog.value = false
                                    },
                                    onDismiss = { showNewlineDialog.value = false },
                                )
                            }
                            if (showZoomDialog.value) {
                                ZoomDialog(
                                    current = zoomPercent.value,
                                    onSelect = { percent ->
                                        applyZoom(percent)
                                        showZoomDialog.value = false
                                    },
                                    onDismiss = { showZoomDialog.value = false },
                                )
                            }
                            if (showExport.value) {
                                androidx.compose.material3.AlertDialog(
                                    onDismissRequest = { showExport.value = false },
                                    confirmButton = {},
                                    title = {},
                                    text = {
                                        ExportDialog(
                                            defaultName = activeTitle.value,
                                            onDismiss = { showExport.value = false },
                                            onExport = { name, options ->
                                                showExport.value = false
                                                performExport(name, options)
                                            },
                                        )
                                    },
                                )
                            }
                            showLanguageDialog.value?.let { (position, current) ->
                                androidx.compose.material3.AlertDialog(
                                    onDismissRequest = { showLanguageDialog.value = null },
                                    title = { Text("声明代码语言") },
                                    text = {
                                        // 预设列表与 macOS CodeBlockLanguageCatalog 对齐；
                                        // 输入框保持可编辑（列表只是便利项，非白名单）。
                                        @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
                                        Column {
                                            androidx.compose.material3.OutlinedTextField(
                                                value = languageInput.value,
                                                onValueChange = { languageInput.value = it },
                                                label = { Text("语言") },
                                                singleLine = true,
                                            )
                                            @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
                                            androidx.compose.foundation.layout.FlowRow(
                                                modifier = Modifier
                                                    .fillMaxWidth()
                                                    .padding(top = 10.dp),
                                                horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
                                                verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(2.dp),
                                            ) {
                                                COMMON_CODE_LANGUAGES.forEach { language ->
                                                    androidx.compose.material3.FilterChip(
                                                        selected = languageInput.value == language,
                                                        onClick = { languageInput.value = language },
                                                        label = { Text(language) },
                                                    )
                                                }
                                            }
                                        }
                                    },
                                    confirmButton = {
                                        TextButton(onClick = {
                                            bridge.post(command("setCodeBlockLanguageAt", """{"position":POSITION,"language":"LANG"}""".replace("POSITION", position.toString()).replace("LANG", languageInput.value)))
                                            showLanguageDialog.value = null
                                        }) { Text("确定") }
                                    },
                                    dismissButton = {
                                        TextButton(onClick = { showLanguageDialog.value = null }) { Text("取消") }
                                    },
                                )
                            }
                        }
                    }
                }
            }
        }

        bridge.onEditorMessageDispatcher = ::handleEditorMessage
        webView.loadUrl(EditorWebViewFactory.EDITOR_URL)
    }

    /** 当前文档目录（SAF document uri 字符串），供图片拦截器解析。 */
    private fun activeDocumentDirectory(): String {
        val active = controller?.activeRef
        return if (active == null) "" else documentDirectoryOf(active)
    }

    private fun documentDirectoryOf(ref: DocumentRef.Saf): String =
        ref.uri.substringBeforeLast('/').substringAfterLast("%2F", "Documents")

    /** 渲染进程崩溃恢复：销毁重建 WebView 并重置会话（spec §8）。 */
    private fun handleRenderProcessGone(goneWebView: WebView): Boolean {
        Log.e(TAG, "WebView 渲染进程崩溃，重建")
        val container = goneWebView.parent as? android.view.ViewGroup ?: return false
        container.removeAllViews()
        goneWebView.destroy()
        val rebuilt = EditorWebViewFactory.create(this, assetResolver = resolver)
        rebuiltWebView = rebuilt
        container.addView(rebuilt)
        // 重建后 ready 握手会重新走（前端重新初始化）
        session.startDocument(session.documentId, revision = 0)
        return true
    }

    private fun refreshWorkspace() {
        val root = workspaceRoot ?: return
        lifecycleScope.launch {
            runCatching { source.listChildren(root) }
                .onSuccess { entries ->
                    sidebarEntries.value = entries.filter { it.isFolder || isMarkdownName(it.name) }
                }
                .onFailure { Log.w(TAG, "列目录失败: ${it.message}") }
        }
    }

    private fun openDocument(ref: DocumentRef.Saf) {
        val active = controller ?: DocumentController(source, bridge::post, session).also {
            controller = it
        }
        lifecycleScope.launch {
            when (val result = active.open(ref)) {
                is DocumentController.OpenResult.Loaded -> {
                    activeTitle.value = result.name
                    dirty.value = false
                    Log.i(TAG, "打开 ${result.name} 编码=${result.encoding.name}")
                }
                DocumentController.OpenResult.PermissionLost -> {
                    showToast("文档权限失效，请重新打开")
                }
                is DocumentController.OpenResult.Failed -> {
                    showToast("打开失败: ${result.message}")
                }
            }
        }
    }

    private suspend fun saveActiveDocument() {
        val active = controller ?: return
        val externalChanged = active.detectExternalChange()
        when (val result = active.save(
            snapshotProvider = { requestSnapshotBlocking() },
            dirty = dirty.value,
            externalChanged = externalChanged,
        )) {
            DocumentController.SaveResult.Saved -> {
                dirty.value = false
                active.discardRecovery(recoveryStore)
                showToast("已保存")
            }
            DocumentController.SaveResult.Conflict -> showToast("文件已被外部修改，请另存或覆盖")
            DocumentController.SaveResult.PermissionLost -> showToast("保存失败：权限失效")
            DocumentController.SaveResult.NotDirty -> Unit
            is DocumentController.SaveResult.Failed -> showToast("保存失败: ${result.message}")
        }
    }

    /** requestSnapshot → snapshot 回环（挂起等待宿主消息）。 */
    private suspend fun requestSnapshotBlocking(): EditorMessage.Snapshot.SnapshotPayload {
        val deferred = kotlinx.coroutines.CompletableDeferred<EditorMessage.Snapshot.SnapshotPayload>()
        pendingSnapshot = deferred
        bridge.post(
            HostMessage.RequestSnapshot(documentId = session.documentId, revision = session.confirmedRevision),
        )
        return deferred.await()
    }

    private fun performExport(name: String, options: ExportOptions) {
        when (options.format) {
            ExportFormat.PDF -> {
                if (options.printFriendly) {
                    bridge.post(command("setPrintFriendly", "true"))
                }
                PdfExporter.printToPdf(this, webView, name, options.landscape)
            }
            ExportFormat.HTML -> createHtmlLauncher.launch("$name.html")
        }
    }

    /** HTML 导出：exportDocument 命令 → exportContent 回环（挂起等待）。 */
    private suspend fun exportHtmlBlocking(): String {
        val deferred = kotlinx.coroutines.CompletableDeferred<String>()
        pendingExportHtml = deferred
        bridge.post(
            HostMessage.Command(
                documentId = session.documentId,
                revision = session.confirmedRevision,
                requestId = session.registerRequest("exportContent"),
                payload = HostMessage.Command.CommandPayload(command = "exportDocument"),
            ),
        )
        return deferred.await()
    }

    private fun command(name: String, text: String? = null): HostMessage.Command = HostMessage.Command(
        documentId = session.documentId,
        revision = session.confirmedRevision,
        payload = HostMessage.Command.CommandPayload(command = name, text = text),
    )

    private fun handleEditorMessage(message: EditorMessage) {
        when (message) {
            is EditorMessage.Ready -> {
                // 前端 ready 携带自生成占位 documentId；宿主必须跟随该 id
                // 开启会话（对齐 Windows：loadDocument 用前端 documentId）。
                val frontendId = message.documentId
                session.startDocument(frontendId, revision = 0)
                welcomeDocumentId = frontendId
                Log.i(TAG, "editor ready; adopting documentId=$frontendId")
                // 主题样式：baseCss 按序拼接 base → 颜色主题 → serif，保证
                // --ml-*/--bg-* 变量与排版规则完整且覆盖顺序正确。
                bridge.post(
                    HostMessage.ApplyStyles(
                        documentId = frontendId,
                        revision = 0,
                        payload = HostMessage.ApplyStyles.ApplyStylesPayload(
                            baseCss = StyleAssets.readBaseCss(this) + "\n" +
                                StyleAssets.readColorThemeCss(this) + "\n" +
                                StyleAssets.readTypographyCss(this),
                            styles = styleCatalogJson(),
                            activeStyle = StyleAssets.DEFAULT_TYPOGRAPHY_ID,
                        ),
                    ),
                )
                bridge.post(welcomeDocumentMessage())
            }
            is EditorMessage.StylesApplied -> Log.i(TAG, "styles applied")
            is EditorMessage.DocumentLoaded -> {
                Log.i(TAG, "document loaded at revision ${message.revision}")
                documentLoaded.value = true
            }
            is EditorMessage.Snapshot -> {
                lastKnownMarkdown = message.payload.markdown
                pendingSnapshot?.complete(message.payload)
                pendingSnapshot = null
            }
            is EditorMessage.ExportContent -> {
                val html = (message.payload as? kotlinx.serialization.json.JsonObject)
                    ?.get("html")?.let { (it as kotlinx.serialization.json.JsonPrimitive).content } ?: ""
                pendingExportHtml?.complete(html)
                pendingExportHtml = null
            }
            is EditorMessage.CopyCodeBlockRequested -> {
                dismissKeyboard()
                val clipboard = getSystemService(android.content.Context.CLIPBOARD_SERVICE)
                    as android.content.ClipboardManager
                clipboard.setPrimaryClip(
                    android.content.ClipData.newPlainText("code", message.payload.text),
                )
                showToast("代码已复制")
            }
            is EditorMessage.CodeBlockLanguageRequested -> {
                // 点击语言标签会顺带聚焦 WebView 编辑区弹出键盘，盖住对话框，先收起
                dismissKeyboard()
                showLanguageDialog.value = Pair(message.payload.position, message.payload.language)
            }
            is EditorMessage.CodeFormatRequested -> {
                // Android 第一版无外部代码格式化器：明示不可用（不静默吞）。
                dismissKeyboard()
                showToast("未配置代码格式化器")
            }
            is EditorMessage.RequestSave -> lifecycleScope.launch { saveActiveDocument() }
            is EditorMessage.OpenLink -> {
                val url = (message.payload as? kotlinx.serialization.json.JsonObject)
                    ?.get("url")?.let { (it as kotlinx.serialization.json.JsonPrimitive).content }
                if (!url.isNullOrEmpty()) {
                    runCatching {
                        startActivity(
                            android.content.Intent(android.content.Intent.ACTION_VIEW, android.net.Uri.parse(url)),
                        )
                    }.onFailure { showToast("无法打开链接") }
                }
            }
            is EditorMessage.CommandStateChanged -> {
                editorActions.value = message.payload.actions.mapValues { (_, state) ->
                    Pair(state.enabled, state.checked)
                }
                editorSourceMode.value = message.payload.sourceMode
            }
            is EditorMessage.DirtyChanged -> dirty.value = message.payload.dirty
            is EditorMessage.OutlineChanged -> outline.value = parseOutline(message.payload)
            is EditorMessage.EditorStatusChanged ->
                editorStatus.value = message.payload as? kotlinx.serialization.json.JsonObject
            else -> Unit
        }
    }

    /** 应用缩放：宿主持有 zoom 偏好，组装 payload 调前端 __markleafApplyVisualVariables
     *  （字段集合与 macOS applyVisualVariables 相同；Android 无排版设置 UI，
     *  基准值取排版默认：fontSize 16 / lineHeight 1.75 / maxWidth 740 / sourceFontSize 14）。 */
    private fun applyZoom(percent: Int) {
        val clamped = percent.coerceIn(50, 200)
        zoomPercent.value = clamped
        val factor = clamped / 100.0
        val payload = "{" +
            "\"lineHeight\":'1.75'," +
            "\"visualScale\":${"%.4f".format(factor)}," +
            "\"fontSize\":'${"%.2f".format(16 * factor)}px'," +
            "\"maxWidth\":'${"%.2f".format(740 * factor)}px'," +
            "\"sourceFontSize\":'${"%.2f".format(14 * factor)}px'," +
            "\"sourceFontFamily\":'monospace'," +
            "\"cjkLanguage\":'zh'," +
            "\"visualCjkAutoSpacing\":true," +
            "\"allowTableColumnResize\":true," +
            "\"visualAdaptiveJustification\":false," +
            "\"ignoreMaxWidth\":false," +
            "\"usePointerAnchor\":false," +
            "\"anchorX\":null,\"anchorY\":null}"
        webView.evaluateJavascript(
            "typeof window.__markleafApplyVisualVariables === 'function' && window.__markleafApplyVisualVariables($payload);",
            null,
        )
    }

    /** 双指捏合：factor 为本次增量；finished 表示手势结束（补齐被限流的末次应用）。 */
    private fun onPinchZoom(factor: Float, finished: Boolean) {
        pinchAccumulator = (pinchAccumulator * factor).coerceIn(50.0, 200.0)
        val now = System.currentTimeMillis()
        val target = pinchAccumulator.roundToInt()
        if (finished || (target != zoomPercent.value && now - lastPinchApplyAt > 120)) {
            applyZoom(target)
            pinchAccumulator = target.toDouble()
            lastPinchApplyAt = now
        }
    }

    /** 瞬态提示（Toast）：状态栏字段位不被临时消息挤占。 */
    private fun showToast(message: String) {
        android.widget.Toast.makeText(this, message, android.widget.Toast.LENGTH_SHORT).show()
    }

    /** 收起软键盘（WebView 点击代码块控件会顺带聚焦编辑区把键盘带出来）。
     *  Chromium 与宿主收起指令存在同帧竞态（show 请求晚于 hide），延迟落刀并
     *  清除 WebView 焦点避免其再次请求 IME。 */    private fun dismissKeyboard() {
        window.decorView.postDelayed({
            webView?.clearFocus()
            WindowCompat.getInsetsController(window, window.decorView)
                .hide(androidx.core.view.WindowInsetsCompat.Type.ime())
        }, 250)
    }

    /** outlineChanged 载荷 → OutlineItem（容错解析，字段缺失跳过）。 */
    private fun parseOutline(payload: kotlinx.serialization.json.JsonElement?): List<OutlineItem> {
        val items = mutableListOf<OutlineItem>()
        val array = (payload as? kotlinx.serialization.json.JsonArray) ?: return items
        for (element in array) {
            val obj = element as? kotlinx.serialization.json.JsonObject ?: continue
            val text = obj["text"]?.let { (it as kotlinx.serialization.json.JsonPrimitive).content } ?: continue
            val position = (obj["position"] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull() ?: 0
            val level = (obj["level"] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull() ?: 1
            items += OutlineItem(position = position, level = level, text = text)
        }
        return items
    }

    /** 排版样式目录载荷：serif（依赖 base）。默认主题走 colorThemeCss。 */
    private fun styleCatalogJson(): kotlinx.serialization.json.JsonElement {
        val typographyCss = assets.open(StyleAssets.TYPOGRAPHY_CSS).use { it.readBytes().decodeToString() }
        return kotlinx.serialization.json.buildJsonArray {
            add(
                kotlinx.serialization.json.buildJsonObject {
                    put("id", kotlinx.serialization.json.JsonPrimitive(StyleAssets.DEFAULT_TYPOGRAPHY_ID))
                    put("displayName", kotlinx.serialization.json.JsonPrimitive(StyleAssets.DEFAULT_TYPOGRAPHY_ID))
                    put("css", kotlinx.serialization.json.JsonPrimitive(typographyCss))
                    put("dependsOn", kotlinx.serialization.json.JsonPrimitive("base"))
                },
            )
        }
    }

    private fun welcomeDocumentMessage(): HostMessage.LoadDocument = HostMessage.LoadDocument(
        documentId = welcomeDocumentId,
        revision = 0,
        payload = HostMessage.LoadDocument.LoadDocumentPayload(
            markdown = WELCOME_MARKDOWN,
            readOnly = false,
        ),
    )

    override fun onPause() {
        super.onPause()
        // spec §4.3：切后台写恢复快照
        if (dirty.value && lastKnownMarkdown.isNotEmpty()) {
            controller?.writeRecovery(recoveryStore, lastKnownMarkdown, dirty = true)
        }
    }

    companion object {
        private const val TAG = "MainActivity"
        private const val WELCOME_DOCUMENT_ID = "welcome"
    }
}

@Composable
private fun TabStrip(
    active: String,
    dirty: Boolean,
    documentLoaded: Boolean,
    onOpenFolder: () -> Unit,
    onOpenFile: () -> Unit,
    onSave: () -> Unit,
    onExport: () -> Unit,
    onOpenDrawer: () -> Unit,
) {
    val marker = if (dirty) "●" else ""
    androidx.compose.foundation.layout.Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(horizontal = 8.dp, vertical = 4.dp),
    ) {
        TextButton(onClick = onOpenDrawer) { Text("☰") }
        Text(
            text = "$marker $active",
            modifier = Modifier
                .weight(1f)
                .padding(vertical = 12.dp),
            style = MaterialTheme.typography.titleMedium,
        )
        TextButton(onClick = onOpenFile) { Text("打开") }
        TextButton(onClick = onSave) { Text("保存") }
        TextButton(onClick = onExport) { Text("导出") }
        TextButton(onClick = onOpenFolder) { Text("📁") }
    }
}

@Composable
private fun androidx.compose.foundation.layout.ColumnScope.EditorContainer(
    webView: WebView,
    onPinch: (Float, Boolean) -> Unit,
) {
    AndroidView(
        factory = {
            // 双指捏合缩放：OnTouchListener 位于 View 分发链最前，能看到原始多点
            // 事件；返回 false 不消费，WebView 的滚动/点击照常。
            val detector = android.view.ScaleGestureDetector(
                webView.context,
                object : android.view.ScaleGestureDetector.SimpleOnScaleGestureListener() {
                    override fun onScale(detector: android.view.ScaleGestureDetector): Boolean {
                        onPinch(detector.scaleFactor, false)
                        return true
                    }

                    override fun onScaleEnd(detector: android.view.ScaleGestureDetector) {
                        onPinch(1f, true)
                    }
                },
            )
            webView.setOnTouchListener { _, event ->
                detector.onTouchEvent(event)
                false
            }
            webView
        },
        modifier = Modifier
            .fillMaxWidth()
            .weight(1f),
    )
}

@Composable
private fun StatusBarPlaceholder(
    status: kotlinx.serialization.json.JsonObject?,
    encoding: String,
    newline: String,
    zoom: Int,
    sourceMode: Boolean,
    onShowStatistics: () -> Unit,
    onEncoding: () -> Unit,
    onNewline: () -> Unit,
    onZoom: () -> Unit,
    onToggleMode: () -> Unit,
) {
    fun field(name: String): String =
        (status?.get(name) as? kotlinx.serialization.json.JsonPrimitive)?.content ?: ""
    val line = field("line")
    val column = field("column")
    val characterCount = field("characterCount")
    val blockType = blockTypeDisplayName(field("blockType"))
    val leading = listOfNotNull(
        if (line.isNotEmpty()) "行 $line 列 $column" else null,
        blockType.ifEmpty { null },
    ).joinToString(" · ")
    // 单行状态栏（对齐桌面字段集）：位置·块类型 | 编码 | 换行 | 缩放% | 模式 …… 字符。
    // 瞬态消息（已保存/已复制/错误）改用 Toast，不挤占字段位。
    androidx.compose.foundation.layout.Row(
        modifier = Modifier
            .fillMaxWidth()
            .navigationBarsPadding()
            .height(26.dp)
            .padding(horizontal = 10.dp, vertical = 4.dp),
        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
    ) {
        Text(
            text = leading,
            // 占满剩余空间：字段组从屏幕右缘开始排
            modifier = Modifier.weight(1f),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
        )
        val itemStyle = MaterialTheme.typography.labelSmall
        val itemColor = MaterialTheme.colorScheme.onSurfaceVariant
        Text(
            text = encoding,
            modifier = Modifier
                .clickable(onClick = onEncoding)
                .padding(start = 10.dp),
            style = itemStyle, color = itemColor,
        )
        Text(
            text = newline,
            modifier = Modifier
                .clickable(onClick = onNewline)
                .padding(start = 10.dp),
            style = itemStyle, color = itemColor,
        )
        Text(
            text = if (sourceMode) "源码" else "可视化",
            modifier = Modifier
                .clickable(onClick = onToggleMode)
                .padding(start = 10.dp),
            style = itemStyle, color = itemColor,
        )
        Text(
            text = "$zoom%",
            modifier = Modifier
                .clickable(onClick = onZoom)
                .padding(start = 10.dp),
            style = itemStyle, color = itemColor,
        )
        if (characterCount.isNotEmpty()) {
            // 对齐桌面：字数按钮点击弹出文档统计面板
            Text(
                text = "$characterCount 字符",
                modifier = Modifier
                    .clickable(onClick = onShowStatistics)
                    .padding(start = 10.dp),
                style = itemStyle,
            )
        }
    }
}

/** 块类型内部名 → 桌面同款显示名（对齐 macOS EditorSession.blockTypeDisplayName）。 */
private fun blockTypeDisplayName(blockType: String): String = when (blockType) {
    "paragraph" -> "正文"
    "heading1" -> "标题 1"
    "heading2" -> "标题 2"
    "heading3" -> "标题 3"
    "heading4" -> "标题 4"
    "heading5" -> "标题 5"
    "heading6" -> "标题 6"
    "blockquote" -> "引用"
    "codeBlock" -> "代码块"
    "bulletList" -> "无序列表"
    "orderedList" -> "有序列表"
    "taskList" -> "任务列表"
    "table" -> "表格"
    "image" -> "图片"
    "footnoteDefinition" -> "脚注"
    else -> blockType
}

/** 保存写回编码选项（对齐桌面 DocumentEncodingPolicy）。 */
private val ENCODING_OPTIONS = listOf(
    "UTF-8" to com.markleaf.app.documents.EncodingInfo("UTF-8", hasBom = false),
    "UTF-8 BOM" to com.markleaf.app.documents.EncodingInfo("UTF-8", hasBom = true),
    "UTF-16LE" to com.markleaf.app.documents.EncodingInfo("UTF-16LE", hasBom = true),
    "UTF-16BE" to com.markleaf.app.documents.EncodingInfo("UTF-16BE", hasBom = true),
    "GBK" to com.markleaf.app.documents.EncodingInfo("GBK", hasBom = false),
)

@Composable
private fun EncodingDialog(
    current: String,
    onSelect: (com.markleaf.app.documents.EncodingInfo) -> Unit,
    onDismiss: () -> Unit,
) {
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = onDismiss) { Text("关闭") } },
        title = { Text("保存编码") },
        text = {
            Column {
                ENCODING_OPTIONS.forEach { (label, info) ->
                    androidx.compose.foundation.layout.Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                onSelect(info)
                            }
                            .padding(vertical = 6.dp),
                        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                    ) {
                        androidx.compose.material3.RadioButton(
                            selected = current == label,
                            onClick = { onSelect(info) },
                        )
                        Text(label, style = MaterialTheme.typography.bodyMedium)
                    }
                }
            }
        },
    )
}

@Composable
private fun NewlineDialog(
    current: String,
    onSelect: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = onDismiss) { Text("关闭") } },
        title = { Text("换行符") },
        text = {
            Column {
                listOf("LF" to "LF（Unix/macOS）", "CRLF" to "CRLF（Windows）").forEach { (value, label) ->
                    androidx.compose.foundation.layout.Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onSelect(value) }
                            .padding(vertical = 6.dp),
                        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                    ) {
                        androidx.compose.material3.RadioButton(
                            selected = current == value,
                            onClick = { onSelect(value) },
                        )
                        Text(label, style = MaterialTheme.typography.bodyMedium)
                    }
                }
            }
        },
    )
}

@Composable
private fun ZoomDialog(
    current: Int,
    onSelect: (Int) -> Unit,
    onDismiss: () -> Unit,
) {
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = onDismiss) { Text("关闭") } },
        title = { Text("缩放") },
        text = {
            Column {
                Text(
                    text = "也可在编辑区双指捏合连续缩放",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                androidx.compose.foundation.layout.Spacer(modifier = Modifier.height(8.dp))
                androidx.compose.foundation.layout.FlowRow(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalArrangement = Arrangement.spacedBy(2.dp),
                ) {
                    listOf(50, 75, 90, 100, 110, 125, 150, 175, 200).forEach { percent ->
                        androidx.compose.material3.FilterChip(
                            selected = current == percent,
                            onClick = { onSelect(percent) },
                            label = { Text("$percent%") },
                        )
                    }
                }
            }
        },
    )
}

/** 文档统计面板：字段与行序对齐桌面 DocumentStatisticsWindowController。 */
@Composable
private fun DocumentStatisticsDialog(
    status: kotlinx.serialization.json.JsonObject?,
    encoding: String,
    newline: String,
    onDismiss: () -> Unit,
) {
    fun field(name: String): String =
        (status?.get(name) as? kotlinx.serialization.json.JsonPrimitive)?.content ?: "—"
    val rows: List<Pair<String, String>> = listOf(
        "字符数" to field("characterCount"),
        "已选字符数" to field("selectedCharacterCount"),
        "总字符数" to field("totalCharacterCount"),
        "非空白字符数" to field("nonWhitespaceCharacterCount"),
        "CJK 字符数" to field("cjkCharacterCount"),
        "西文单词数" to field("westernWordCount"),
        "公式数" to field("formulaCount"),
        "代码行数" to field("codeLineCount"),
        "段落数" to field("paragraphCount"),
        "行" to field("line"),
        "列" to field("column"),
        "块类型" to blockTypeDisplayName(field("blockType")),
        "编码" to encoding,
        "换行符" to newline,
    )
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(onClick = onDismiss) { Text("关闭") }
        },
        title = { Text("文档统计") },
        text = {
            Column {
                rows.forEach { (label, value) ->
                    androidx.compose.foundation.layout.Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp),
                        horizontalArrangement = androidx.compose.foundation.layout.Arrangement.SpaceBetween,
                    ) {
                        Text(
                            text = label,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        Text(
                            text = value,
                            style = MaterialTheme.typography.bodyMedium,
                        )
                    }
                }
            }
        },
    )
}

/** 代码块语言预设：与 macOS CodeBlockLanguageCatalog.commonLanguages 对齐。
 *  输入框仍可自由填写，列表只是便利项而非白名单。 */
private val COMMON_CODE_LANGUAGES = listOf(
    "swift", "c", "cpp", "objective-c", "java", "kotlin", "python",
    "javascript", "typescript", "jsx", "tsx", "html", "css", "json",
    "yaml", "xml", "shell", "powershell", "sql", "markdown", "verilog",
    "latex", "go", "rust", "php", "ruby", "r", "toml", "ini", "diff",
    "mermaid",
)

/** 内置欢迎文档：覆盖表格/公式/mermaid/代码块/任务列表的渲染验收面。 */
private val WELCOME_MARKDOWN = """
# 欢迎使用 MarkLeaf

这是一段 **加粗**、*斜体*、~~删除线~~ 与 `行内代码` 的示例。

## 表格

| 平台 | 状态 |
| --- | --- |
| macOS | 已发布 |
| Windows | 已发布 |
| Android | 构建中 |

## 数学公式

行内公式 ${'$'}E = mc^2$；块级公式：

$$\int_0^1 x^2 \, dx = \frac{1}{3}$$

## 代码块

```kotlin
fun main() {
    println("Hello from MarkLeaf Android")
}
```

## 任务列表

- [x] WebView 骨架
- [x] 协议桥
- [x] 编辑闭环（进行中）

## Mermaid

```mermaid
graph LR
    A[打开文档] --> B[编辑]
    B --> C[导出]
```
""".trim()
