@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app

import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.offset
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
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.platform.LocalConfiguration
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
import com.markleaf.app.documents.DocumentCodings
import com.markleaf.app.documents.DocumentController
import com.markleaf.app.documents.DocumentEntry
import com.markleaf.app.documents.DocumentRef
import com.markleaf.app.documents.EncodingInfo
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
import com.markleaf.app.ui.TopMenuBar
import com.markleaf.app.ui.TopMenuEntry
import com.markleaf.app.ui.TopMenuSection
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
    // 切模式瞬间前端会连发多条 commandStateChanged（先旧后新），逐条应用会让
    // 状态栏文字来回闪；防抖合并只应用停顿后的最后一条
    private val sourceModeDisplayHandler = android.os.Handler(android.os.Looper.getMainLooper())
    private var pendingSourceMode = false
    private val commitSourceModeRunnable = Runnable {
        editorSourceMode.value = pendingSourceMode
    }
    private val zoomPercent = mutableStateOf(100)
    private val documentEncoding = mutableStateOf("UTF-8")
    private val documentNewline = mutableStateOf("LF")
    private val showEncodingDialog = mutableStateOf(false)
    private val showNewlineDialog = mutableStateOf(false)
    private val showZoomDialog = mutableStateOf(false)
    private val showPreferences = mutableStateOf(false)
    private val showAbout = mutableStateOf(false)
    /** 启动恢复：待用户决策的快照（恢复 / 另存副本 / 丢弃）。 */
    private val recoveryEntry = mutableStateOf<RecoveryStore.Entry?>(null)
    private var pendingRecoveryMarkdown: String? = null
    private var pendingRecoveryId: String? = null
    private val showCloseConfirm = mutableStateOf(false)
    private val showLinkDialog = mutableStateOf(false)
    private var linkInput = ""
    private val renameEntryDialog = mutableStateOf<DocumentEntry?>(null)
    private var renameEntryInput = ""
    private val deleteDocConfirm = mutableStateOf(false)
    private val renameDocDialog = mutableStateOf(false)
    private var renameDocInput = ""
    private val openImageLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent(),
    ) { sourceUri ->
        sourceUri ?: return@registerForActivityResult
        lifecycleScope.launch { insertImageFromSaf(sourceUri) }
    }
    private val androidReadOnly = mutableStateOf(false)
    private var pendingReadOnlyOpen = false
    private val saveAsLauncher = registerForActivityResult(
        ActivityResultContracts.CreateDocument("text/markdown"),
    ) { uri ->
        uri ?: return@registerForActivityResult
        lifecycleScope.launch {
            runCatching {
                val markdown = requestSnapshotBlocking().markdown
                val ref = DocumentRef.Saf(uri.toString())
                source.write(ref, DocumentCodings.encode(markdown, "UTF-8"))
                controller?.adoptRef(ref)
                ref
            }.onSuccess { savedRef ->
                activeTitle.value = Uri.parse(savedRef.uri).lastPathSegment?.substringAfterLast("%2F") ?: "未命名"
                dirty.value = false
                showToast("已另存为 ${activeTitle.value}")
                rememberRecentFile(savedRef.uri, activeTitle.value)
            }.onFailure { showToast("另存为失败：${it.message}") }
        }
    }
    private lateinit var prefsStore: android.content.SharedPreferences
    private val editorPrefs = mutableStateOf(com.markleaf.app.ui.EditorPrefs())
    private val selectionToolbarRect = mutableStateOf<androidx.compose.ui.geometry.Rect?>(null)
    private val selectionToolbarVisible = mutableStateOf(false)
    private val editorSemanticContext = mutableStateOf<String?>(null)
    private val showToolbarLanguageDialog = mutableStateOf(false)
    private var toolbarLanguageInput = ""
    private val toolbarHandler = android.os.Handler(android.os.Looper.getMainLooper())
    private val showSelectionToolbarRunnable = Runnable {
        if (selectionToolbarRect.value != null) {
            dismissKeyboard()
            selectionToolbarVisible.value = true
        }
    }
    private var pendingSelectionExport: kotlinx.coroutines.CompletableDeferred<String>? = null
    private var pendingSelectionExportRequestId: String? = null
    private var pinchAccumulator = 100.0
    private var lastPinchApplyAt = 0L
    private var lastAppliedStyleId = "serif"
    private val sidebarEntries = mutableStateOf<List<DocumentEntry>>(emptyList())
    private val outline = mutableStateOf<List<OutlineItem>>(emptyList())
    private val activeTitle = mutableStateOf("未命名")
    private val showExport = mutableStateOf(false)
    private val showLanguageDialog = mutableStateOf<Pair<Long, String>?>(null)
    private val languageInput = mutableStateOf("")
    private val editorActions = mutableStateOf<Map<String, Pair<Boolean, Boolean>>>(emptyMap())
    private var welcomeDocumentId = WELCOME_DOCUMENT_ID
    private var lastKnownMarkdown = ""
    private var pendingSnapshot: kotlinx.coroutines.CompletableDeferred<EditorMessage.Snapshot.SnapshotPayload>? = null
    private var pendingExportHtml: kotlinx.coroutines.CompletableDeferred<String>? = null
    private var rebuiltWebView: WebView? = null
    private lateinit var resolver: AssetResolver
    /** 编辑容器实际挂载的 WebView（崩溃重建后切换，EditorContainer 的 update 跟随）。 */
    private val activeWebViewState = mutableStateOf<WebView?>(null)
    /** 渲染进程崩溃保留的内容：ready 握手后重放，避免回落到欢迎文档。 */
    private var pendingCrashRestoreMarkdown: String? = null

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
        activeWebViewState.value = webView
        session = EditorSessionState().apply { startDocument(WELCOME_DOCUMENT_ID) }
        source = SafDocumentSource(this)
        recoveryStore = RecoveryStore(this)
        prefsStore = getSharedPreferences("markleaf-settings", MODE_PRIVATE)
        editorPrefs.value = com.markleaf.app.ui.EditorPrefs.load(prefsStore)
        zoomPercent.value = editorPrefs.value.zoom
        bridge = EditorBridge(
            postScript = { script -> webView.evaluateJavascript(script, null) },
            session = session,
        )
        bridge.attach(webView)

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    Box(modifier = Modifier.fillMaxSize()) {
                    val drawerState = rememberDrawerState(DrawerValue.Closed)
                    val scope = rememberCoroutineScope()
                    ModalNavigationDrawer(
                        drawerState = drawerState,
                        // 打开时允许手势：点击遮罩/右滑即可关闭；
                        // 关闭时禁手势：长按选字/拖拽常从屏幕左缘起步，抽屉会误触发。
                        // 抽屉仅经 ☰ 打开
                        gesturesEnabled = drawerState.targetValue == DrawerValue.Open,
                        drawerContent = {
                            androidx.compose.material3.ModalDrawerSheet(
                                modifier = Modifier.width(
                                    minOf(
                                        300.dp,
                                        (LocalConfiguration.current.screenWidthDp.dp * 0.78f),
                                    )
                                ),
                            ) {
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
                                    onCreateDocument = {
                                        scope.launch { drawerState.close() }
                                        newDocument("markdown")
                                    },
                                    onRefresh = { refreshWorkspace() },
                                    onEntryLongPress = { entry ->
                                        renameEntryInput = entry.name
                                        renameEntryDialog.value = entry
                                    },
                                )
                            }
                        },
                    ) {
                        Column(
                            modifier = Modifier
                                .fillMaxSize()
                                // 键盘展开时整体收起到底栏上方：状态栏与编辑区可见
                                .imePadding(),
                        ) {
                            TopMenuBar(
                                active = activeTitle.value,
                                dirty = dirty.value,
                                undoEnabled = editorActions.value["undo"]?.first == true,
                                redoEnabled = editorActions.value["redo"]?.first == true,
                                sections = topMenuSections(
                                    onToggleSidebar = {
                                        scope.launch {
                                            if (drawerState.isClosed) drawerState.open() else drawerState.close()
                                        }
                                    },
                                ),
                                onOpenDrawer = { scope.launch { drawerState.open() } },
                                onUndo = { postCommand("undo") },
                                onRedo = { postCommand("redo") },
                                onSave = { lifecycleScope.launch { saveActiveDocument() } },
                            )
                            EditorContainer(
                                activeWebView = activeWebViewState.value,
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
                                    pendingSourceMode = editorSourceMode.value
                                    sourceModeDisplayHandler.removeCallbacks(commitSourceModeRunnable)
                                    sourceModeDisplayHandler.postDelayed(commitSourceModeRunnable, 150)
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
                                        documentEncoding.value = encodingDisplayName(info)
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
            SelectionToolbar(
                rect = selectionToolbarRect.value,
                visible = selectionToolbarVisible.value,
                semanticContext = editorSemanticContext.value,
                requestSelection = { requestSelectionText() },
                actions = editorActions.value,
                onCommand = { cmd, text -> bridge.post(command(cmd, text)) },
                onLanguagePicker = { showToolbarLanguageDialog.value = true },
            )
            if (showToolbarLanguageDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showToolbarLanguageDialog.value = false },
                    title = { Text("声明代码语言") },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = toolbarLanguageInput,
                            onValueChange = { toolbarLanguageInput = it },
                            label = { Text("语言") },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            bridge.post(command("setCodeBlockLanguage", toolbarLanguageInput))
                            showToolbarLanguageDialog.value = false
                        }) { Text("确定") }
                    },
                    dismissButton = {
                        TextButton(onClick = { showToolbarLanguageDialog.value = false }) { Text("取消") }
                    },
                )
            }
            // 首选项层：右滑入场的推页动画；系统返回先关首选项而不是退出应用
            androidx.compose.animation.AnimatedVisibility(
                visible = showPreferences.value,
                enter = androidx.compose.animation.slideInHorizontally(
                    animationSpec = androidx.compose.animation.core.tween(220),
                    initialOffsetX = { it },
                ) + androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(180)),
                exit = androidx.compose.animation.slideOutHorizontally(
                    animationSpec = androidx.compose.animation.core.tween(200),
                    targetOffsetX = { it },
                ) + androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(160)),
            ) {
                com.markleaf.app.ui.PreferencesScreen(
                    prefs = editorPrefs.value,
                    styleIds = StyleAssets.TYPOGRAPHY_STYLES,
                    themeIds = StyleAssets.COLOR_THEMES,
                    onChange = ::updatePrefs,
                    onDismiss = { showPreferences.value = false },
                )
            }
            if (showAbout.value) {
                val version = runCatching {
                    packageManager.getPackageInfo(packageName, 0).versionName
                }.getOrNull() ?: ""
                com.markleaf.app.ui.AboutDialog(version = version, onDismiss = { showAbout.value = false })
            }
            // 系统返回手势：有覆盖层时先关覆盖层（首选项/关于），不要退出应用
            androidx.activity.compose.BackHandler(enabled = showPreferences.value) {
                showPreferences.value = false
            }
            // 关闭当前文档：脏状态先给保存/丢弃选择
            if (showCloseConfirm.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showCloseConfirm.value = false },
                    title = { Text("关闭前保存修改？") },
                    text = { Text("当前文档有未保存的修改。") },
                    confirmButton = {
                        TextButton(onClick = {
                            showCloseConfirm.value = false
                            lifecycleScope.launch {
                                saveActiveDocument()
                                closeDocument()
                            }
                        }) { Text("保存并关闭") }
                    },
                    dismissButton = {
                        androidx.compose.foundation.layout.Row {
                            TextButton(onClick = {
                                showCloseConfirm.value = false
                                closeDocument()
                            }) { Text("不保存") }
                            TextButton(onClick = { showCloseConfirm.value = false }) { Text("取消") }
                        }
                    },
                )
            }
            // 插入超链接：URL 输入（作用于当前选区）
            if (showLinkDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showLinkDialog.value = false },
                    title = { Text("插入超链接") },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = linkInput,
                            onValueChange = { linkInput = it },
                            label = { Text("链接地址") },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            if (linkInput.isNotBlank()) postCommand("setLink", linkInput.trim())
                            showLinkDialog.value = false
                        }) { Text("确定") }
                    },
                    dismissButton = {
                        TextButton(onClick = { showLinkDialog.value = false }) { Text("取消") }
                    },
                )
            }
            // 重命名当前文档（SAF rename）
            if (renameDocDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { renameDocDialog.value = false },
                    title = { Text("重命名") },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = renameDocInput,
                            onValueChange = { renameDocInput = it },
                            label = { Text("新名称") },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            val ref = controller?.activeRef
                            val name = renameDocInput.trim()
                            if (ref != null && name.isNotEmpty()) {
                                renameDocDialog.value = false
                                lifecycleScope.launch {
                                    runCatching { source.rename(ref, name) }
                                        .onSuccess { renamed ->
                                            controller?.adoptRef(renamed)
                                            activeTitle.value = name
                                            showToast("已重命名")
                                        }
                                        .onFailure { showToast("重命名失败：${it.message}") }
                                }
                            }
                        }) { Text("确定") }
                    },
                    dismissButton = {
                        TextButton(onClick = { renameDocDialog.value = false }) { Text("取消") }
                    },
                )
            }
            // 删除当前文档确认
            if (deleteDocConfirm.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { deleteDocConfirm.value = false },
                    title = { Text("删除当前文档？") },
                    text = { Text("将从存储中删除该文件，此操作不可撤销。") },
                    confirmButton = {
                        TextButton(onClick = {
                            deleteDocConfirm.value = false
                            val ref = controller?.activeRef
                            if (ref != null) {
                                lifecycleScope.launch {
                                    runCatching { source.delete(ref) }
                                        .onSuccess {
                                            showToast("已删除")
                                            closeDocument()
                                        }
                                        .onFailure { showToast("删除失败：${it.message}") }
                                }
                            }
                        }) { Text("删除") }
                    },
                    dismissButton = {
                        TextButton(onClick = { deleteDocConfirm.value = false }) { Text("取消") }
                    },
                )
            }
            // 侧栏条目重命名（长按）
            renameEntryDialog.value?.let { entry ->
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { renameEntryDialog.value = null },
                    title = { Text("重命名「${entry.name}」") },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = renameEntryInput,
                            onValueChange = { renameEntryInput = it },
                            label = { Text("新名称") },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            val target = renameEntryDialog.value
                            val name = renameEntryInput.trim()
                            if (target != null && name.isNotEmpty() && name != entry.name) {
                                renameEntryDialog.value = null
                                renameSidebarEntry(target, name)
                            } else {
                                renameEntryDialog.value = null
                            }
                        }) { Text("确定") }
                    },
                    dismissButton = {
                        TextButton(onClick = { renameEntryDialog.value = null }) { Text("取消") }
                    },
                )
            }
            // 启动恢复：最近一份未保存快照（其余在消化后继续弹出）
            recoveryEntry.value?.let { entry ->
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { /* 必须明确选择：恢复 / 另存副本 / 丢弃 */ },
                    title = { Text("发现未保存的修改") },
                    text = {
                        Text(
                            "文档「${recoveryDisplayName(entry)}」在 " +
                                java.text.DateFormat.getDateTimeInstance()
                                    .format(java.util.Date(entry.savedAt)) +
                                " 有未保存的内容。",
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = { restoreRecoverySnapshot(entry) }) { Text("恢复") }
                    },
                    dismissButton = {
                        androidx.compose.foundation.layout.Row {
                            TextButton(onClick = { saveRecoveryCopy(entry) }) { Text("另存副本") }
                            TextButton(onClick = { discardRecoverySnapshot(entry) }) { Text("丢弃") }
                        }
                    },
                )
            }
            }
        }

        bridge.onEditorMessageDispatcher = ::handleEditorMessage
        webView.loadUrl(EditorWebViewFactory.EDITOR_URL)
        routeViewIntent(intent)
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        routeViewIntent(intent)
    }

    /** 外部打开（ACTION_VIEW text/markdown|text/plain|*.md）：把 content uri
     *  交给当前会话打开（singleTask 下经 onNewIntent 到达）。 */
    private fun routeViewIntent(intent: android.content.Intent?) {
        if (intent?.action != android.content.Intent.ACTION_VIEW) return
        val uri = intent.data?.toString() ?: return
        if (uri.isEmpty()) return
        intent.setData(null)
        openDocument(DocumentRef.Saf(uri))
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
        // 保留当前内容：ready 握手后重放（lastKnownMarkdown 为空说明是欢迎文档，走原路径）
        if (lastKnownMarkdown.isNotEmpty()) {
            pendingCrashRestoreMarkdown = lastKnownMarkdown
        }
        val rebuilt = EditorWebViewFactory.create(
            this,
            assetResolver = resolver,
            documentDir = this::activeDocumentDirectory,
            onRenderProcessGone = this::handleRenderProcessGone,
        )
        rebuiltWebView = rebuilt
        webView = rebuilt
        // 新 WebView 重新注册 JS 桥；postScript 捕获的是 webView 属性，已指向新实例
        bridge.attach(rebuilt)
        activeWebViewState.value = rebuilt
        // 旧实例待容器换载后销毁
        goneWebView.post { goneWebView.destroy() }
        // 会话沿用当前 documentId，ready 握手重新走
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
            // 新控制器落首选项的默认编码/换行；打开已有文件时会被探测值覆盖
            encodingInfoFromPref(editorPrefs.value.defaultEncoding)?.let(it::setEncoding)
            it.setNewline(editorPrefs.value.defaultNewline)
            controller = it
        }
        lifecycleScope.launch {
            when (val result = active.open(ref)) {
                is DocumentController.OpenResult.Loaded -> {
                    activeTitle.value = result.name
                    dirty.value = false
                    documentEncoding.value = encodingDisplayName(result.encoding)
                    documentNewline.value = active.activeNewline
                    Log.i(TAG, "打开 ${result.name} 编码=${result.encoding.name}")
                    // 启动恢复：打开目标文件后用快照内容覆盖
                    val snapshot = pendingRecoveryMarkdown
                    val snapshotId = pendingRecoveryId
                    if (snapshot != null) {
                        pendingRecoveryMarkdown = null
                        pendingRecoveryId = null
                        bridge.post(
                            HostMessage.LoadDocument(
                                documentId = session.documentId,
                                revision = session.confirmedRevision,
                                payload = HostMessage.LoadDocument.LoadDocumentPayload(markdown = snapshot),
                            ),
                        )
                        dirty.value = true
                        snapshotId?.let { recoveryStore.discard(it) }
                        activeTitle.value = "${
                            result.name.substringBeforeLast('.')
                        }（恢复）"
                        showToast("已恢复未保存的修改")
                    }
                }
                DocumentController.OpenResult.PermissionLost -> {
                    showToast("文档权限失效，请重新打开")
                }
                is DocumentController.OpenResult.PermissionLost -> {
                    showToast("无访问权限")
                    // 恢复流程被打断：重新弹对话框让用户重选
                    if (pendingRecoveryMarkdown != null) {
                        pendingRecoveryMarkdown = null
                        pendingRecoveryId = null
                        maybeShowRecoveryDialog()
                    }
                }
                is DocumentController.OpenResult.Failed -> {
                    showToast("打开失败: ${result.message}")
                    if (pendingRecoveryMarkdown != null) {
                        pendingRecoveryMarkdown = null
                        pendingRecoveryId = null
                        maybeShowRecoveryDialog()
                    }
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
                lastAppliedStyleId = editorPrefs.value.styleId
                bridge.post(
                    HostMessage.ApplyStyles(
                        documentId = frontendId,
                        revision = 0,
                        payload = HostMessage.ApplyStyles.ApplyStylesPayload(
                            baseCss = StyleAssets.readBaseCss(this) + "\n" +
                                StyleAssets.readColorThemeCss(this, editorPrefs.value.colorTheme) + "\n" +
                                StyleAssets.readTypographyCss(this, editorPrefs.value.styleId),
                            styles = styleCatalogJson(),
                            activeStyle = editorPrefs.value.styleId,
                        ),
                    ),
                )
                // 崩溃重建：重放崩溃前内容；冷启动：欢迎文档
                val crashMarkdown = pendingCrashRestoreMarkdown
                pendingCrashRestoreMarkdown = null
                bridge.post(
                    if (crashMarkdown != null) {
                        HostMessage.LoadDocument(
                            documentId = frontendId,
                            revision = 0,
                            payload = HostMessage.LoadDocument.LoadDocumentPayload(markdown = crashMarkdown),
                        )
                    } else {
                        welcomeDocumentMessage()
                    },
                )
                // 持久化外观（字号/行高/宽度/缩放）在首帧后应用
                android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(
                    { applyAppearance() },
                    400,
                )
            }
            is EditorMessage.StylesApplied -> Log.i(TAG, "styles applied")
            is EditorMessage.DocumentLoaded -> {
                Log.i(TAG, "document loaded at revision ${message.revision}")
                documentLoaded.value = true
                maybeShowRecoveryDialog()
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
            is EditorMessage.SelectionExport -> {
                // 原生工具条 剪切/拷贝 的取文本回包（requestId 关联）
                if (message.requestId != null && message.requestId == pendingSelectionExportRequestId) {
                    pendingSelectionExport?.complete(
                        (message.payload as? kotlinx.serialization.json.JsonObject)
                            ?.get("text")?.let { (it as kotlinx.serialization.json.JsonPrimitive).content } ?: "",
                    )
                    pendingSelectionExport = null
                    pendingSelectionExportRequestId = null
                }
            }
            is EditorMessage.CommandStateChanged -> {
                editorActions.value = message.payload.actions.mapValues { (_, state) ->
                    Pair(state.enabled, state.checked)
                }
                pendingSourceMode = message.payload.sourceMode
                sourceModeDisplayHandler.removeCallbacks(commitSourceModeRunnable)
                sourceModeDisplayHandler.postDelayed(commitSourceModeRunnable, 120)
                editorSemanticContext.value = message.payload.semanticContext
            }
            is EditorMessage.DirtyChanged -> dirty.value = message.payload.dirty
            is EditorMessage.OutlineChanged -> outline.value = parseOutline(message.payload)
            is EditorMessage.EditorStatusChanged -> {
                val payload = message.payload as? kotlinx.serialization.json.JsonObject
                editorStatus.value = payload
                // 选区矩形（CSS 视口坐标）→ 设备坐标：WebView 原点 + css × dpr
                val origin = IntArray(2).also { webView.getLocationOnScreen(it) }
                val dpr = resources.displayMetrics.density
                val rect = (payload?.get("selectionRect") as? kotlinx.serialization.json.JsonObject)?.let { obj ->
                    fun f(name: String): Float? =
                        (obj[name] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toFloatOrNull()
                    val x = f("x") ?: return@let null
                    val y = f("y") ?: return@let null
                    val w = f("w") ?: return@let null
                    val h = f("h") ?: return@let null
                    androidx.compose.ui.geometry.Rect(
                        origin[0] + x * dpr,
                        origin[1] + y * dpr,
                        origin[0] + (x + w) * dpr,
                        origin[1] + (y + h) * dpr,
                    )
                }
                // 仅长按/双击手势产生的选择显示工具条（单点节点选中/caret 不弹）
                val fromGesture = (payload?.get("selectionFromGesture") as? kotlinx.serialization.json.JsonPrimitive)
                    ?.content == "true"
                selectionToolbarRect.value = if (fromGesture) rect else null
                selectionToolbarVisible.value = false
                toolbarHandler.removeCallbacks(showSelectionToolbarRunnable)
                if (fromGesture && rect != null) {
                    // 拖动选区期间不显示，选区稳定 350ms（≈松手）后弹出；
                    // 弹出时收键盘（双击 focus 会带出 IME）
                    toolbarHandler.postDelayed(showSelectionToolbarRunnable, 350)
                }
            }
            else -> Unit
        }
    }

    /** 应用外观：缩放 + 首选项排版字段一次下发（__markleafApplyVisualVariables，
     *  字段集合与 macOS applyVisualVariables 相同）。缩放因子作用于
     *  fontSize/maxWidth/sourceFontSize；lineHeight 为倍数不参与缩放。 */
    private fun applyAppearance() {
        val prefs = editorPrefs.value
        val percent = zoomPercent.value
        val factor = percent / 100.0
        val maxWidthPx = if (prefs.ignoreMaxWidth) "null" else "'${"%.2f".format(prefs.maxWidth * factor)}px'"
        val payload = "{" +
            "\"lineHeight\":'${"%.2f".format(prefs.lineHeight)}'," +
            "\"visualScale\":${"%.4f".format(factor)}," +
            "\"fontSize\":'${"%.2f".format(prefs.fontSize * factor)}px'," +
            "\"maxWidth\":$maxWidthPx," +
            "\"sourceFontSize\":'${"%.2f".format(prefs.sourceFontSize * factor)}px'," +
            "\"sourceFontFamily\":'monospace'," +
            "\"cjkLanguage\":'zh'," +
            "\"visualCjkAutoSpacing\":${prefs.cjkSpacing}," +
            "\"allowTableColumnResize\":${prefs.tableColumnResize}," +
            "\"visualAdaptiveJustification\":${prefs.adaptiveJustification}," +
            "\"ignoreMaxWidth\":${prefs.ignoreMaxWidth}," +
            "\"usePointerAnchor\":false," +
            "\"anchorX\":null,\"anchorY\":null}"
        webView.evaluateJavascript(
            "typeof window.__markleafApplyVisualVariables === 'function' && window.__markleafApplyVisualVariables($payload);",
            null,
        )
    }

    /** 应用缩放：捏合/菜单共用；仅更新缩放值并下发外观。 */
    private fun applyZoom(percent: Int) {
        zoomPercent.value = percent.coerceIn(50, 200)
        applyAppearance()
    }

    /** 首选项变更：保存 + 立即应用（缩放字段同步 zoomPercent；主题/样式变更重发样式载荷）。 */
    private fun updatePrefs(newPrefs: com.markleaf.app.ui.EditorPrefs) {
        val old = editorPrefs.value
        editorPrefs.value = newPrefs
        newPrefs.save(prefsStore)
        zoomPercent.value = newPrefs.zoom
        if (newPrefs.styleId != old.styleId || newPrefs.colorTheme != old.colorTheme) {
            lastAppliedStyleId = newPrefs.styleId
            applyStyles()
        }
        applyAppearance()
    }

    /** 下发主题样式载荷（base + 当前颜色主题 + 排版样式目录）。 */
    private fun applyStyles() {
        bridge.post(
            HostMessage.ApplyStyles(
                documentId = session.documentId,
                revision = session.confirmedRevision,
                payload = HostMessage.ApplyStyles.ApplyStylesPayload(
                    baseCss = StyleAssets.readBaseCss(this) + "\n" +
                        StyleAssets.readColorThemeCss(this, editorPrefs.value.colorTheme) + "\n" +
                        StyleAssets.readTypographyCss(this, editorPrefs.value.styleId),
                    styles = styleCatalogJson(),
                    activeStyle = editorPrefs.value.styleId,
                ),
            ),
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

    /** 取当前选区纯文本（请求-回包模式，对齐 pendingSnapshot）。 */
    private suspend fun requestSelectionText(): String {
        val requestId = java.util.UUID.randomUUID().toString()
        val deferred = kotlinx.coroutines.CompletableDeferred<String>()
        pendingSelectionExport = deferred
        pendingSelectionExportRequestId = requestId
        bridge.post(
            HostMessage.Command(
                documentId = session.documentId,
                revision = session.confirmedRevision,
                payload = HostMessage.Command.CommandPayload(command = "exportSelection"),
                requestId = requestId,
            ),
        )
        return runCatching { deferred.await() }.getOrDefault("")
    }

    private fun clipboardManager(): android.content.ClipboardManager =
        getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager

    /** 瞬态提示（Toast）：状态栏字段位不被临时消息挤占。 */
    private fun showToast(message: String) {
        android.widget.Toast.makeText(this, message, android.widget.Toast.LENGTH_SHORT).show()
    }

    /** 收起软键盘（WebView 点击代码块控件会顺带聚焦编辑区把键盘带出来）。
     *  Chromium 与宿主收起指令存在同帧竞态（show 请求晚于 hide），延迟落刀并
     *  Chromium 的 IME show 请求可能晚到，单次 hide 会被覆盖——按
     *  250/600/1000ms 多次补刀（不清焦点：长按选择依赖焦点存活）。
     *  hide 对已收起的 IME 是无害空操作。 */
    private fun dismissKeyboard() {
        val controller = WindowCompat.getInsetsController(window, window.decorView)
        val ime = androidx.core.view.WindowInsetsCompat.Type.ime()
        listOf(250L, 600L, 1000L).forEach { delay ->
            window.decorView.postDelayed({
                controller.hide(ime)
            }, delay)
        }
    }

    /** outlineChanged 载荷 → OutlineItem（容错解析，字段缺失跳过）。 */
    private fun parseOutline(payload: kotlinx.serialization.json.JsonElement?): List<OutlineItem> {
        val items = mutableListOf<OutlineItem>()
        val array = (payload as? kotlinx.serialization.json.JsonObject)
            ?.get("headings") as? kotlinx.serialization.json.JsonArray ?: return items
        for (element in array) {
            val obj = element as? kotlinx.serialization.json.JsonObject ?: continue
            val text = obj["text"]?.let { (it as kotlinx.serialization.json.JsonPrimitive).content } ?: continue
            val position = (obj["position"] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull() ?: 0
            val level = (obj["level"] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull() ?: 1
            items += OutlineItem(position = position, level = level, text = text)
        }
        return items
    }

    /** 排版样式目录载荷（衬线/无衬线/极简/笔记本/杂志；依赖 base）。 */
    private fun styleCatalogJson(): kotlinx.serialization.json.JsonElement =
        kotlinx.serialization.json.buildJsonArray {
            StyleAssets.TYPOGRAPHY_STYLES.forEach { (id, display) ->
                add(
                    kotlinx.serialization.json.buildJsonObject {
                        put("id", kotlinx.serialization.json.JsonPrimitive(id))
                        put("displayName", kotlinx.serialization.json.JsonPrimitive(display))
                        put("css", kotlinx.serialization.json.JsonPrimitive(StyleAssets.readTypographyCss(this@MainActivity, id)))
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

    /** 顶栏菜单内容（条目与桌面 NativeMenuBuilder 同名命令对齐）。
     *  可用性共用 editorActions；粘贴走 pasteClipboard（读 Android 剪贴板）。 */
    private fun postCommand(name: String) {
        bridge.post(command(name, ""))
    }

    private fun postCommand(name: String, text: String) {
        bridge.post(command(name, text))
    }

    private fun topMenuSections(
        onToggleSidebar: () -> Unit,
    ): List<TopMenuSection> {
        fun en(cmd: String) = editorActions.value[cmd]?.first == true
        fun ck(cmd: String): Boolean? = editorActions.value[cmd]?.second
        fun entry(
            title: String,
            cmd: String,
            separator: Boolean = false,
            text: String? = null,
            action: (() -> Unit)? = null,
        ) = TopMenuEntry(title, en(cmd), separator, ck(cmd)) {
            if (action != null) action() else postCommand(cmd, text ?: "")
        }
        fun host(
            title: String,
            enabled: Boolean = true,
            separator: Boolean = false,
            checked: Boolean? = null,
            action: () -> Unit = {},
        ) = TopMenuEntry(title, enabled, separator, checked, action)
        val hasDoc = documentLoaded.value
        val hasFile = controller?.activeRef != null
        val recent = recentFiles()
        return listOf(
            TopMenuSection("文件", buildList {
                add(host("新建 Markdown") { newDocument("markdown") })
                add(host("新建纯文本") { newDocument("plainText") })
                add(host("打开文件夹…", separator = true) { openTreeLauncher.launch(null) })
                add(host("打开文件…") { pendingReadOnlyOpen = false; openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) })
                add(host("以只读方式打开…") { pendingReadOnlyOpen = true; openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) })
                if (recent.isNotEmpty()) {
                    add(host("最近文件", separator = true, enabled = false))
                    recent.forEach { recentEntry ->
                        add(host(recentEntry.second, enabled = true) { openDocument(DocumentRef.Saf(recentEntry.first)) })
                    }
                }
                add(host("恢复未保存的文件…", separator = true, enabled = recoveryStore.pendingIds().isNotEmpty()) {
                    maybeShowRecoveryDialog()
                    if (recoveryEntry.value == null) showToast("没有待恢复的内容")
                })
                add(host("保存", separator = true, enabled = hasDoc) { lifecycleScope.launch { saveActiveDocument() } })
                add(host("另存为…", enabled = hasDoc) { saveAsLauncher.launch("未命名.md") })
                add(host("导出…", enabled = hasDoc) { showExport.value = true })
                add(host("关闭当前文档", enabled = hasDoc, separator = true) {
                    if (dirty.value) showCloseConfirm.value = true else closeDocument()
                })
                add(host("关闭工作区", enabled = workspaceRoot != null) { closeWorkspace() })
                add(host("重命名…", enabled = hasFile, separator = true) {
                    renameDocInput = activeTitle.value
                    renameDocDialog.value = true
                })
                add(host("删除", enabled = hasFile) { deleteDocConfirm.value = true })
                add(host("复制文件路径", enabled = hasFile, separator = true) {
                    controller?.activeRef?.let {
                        clipboardManager().setPrimaryClip(android.content.ClipData.newPlainText("path", it.uri))
                        showToast("已复制文件 URI")
                    }
                })
                add(host("复制文件内容", enabled = hasDoc && lastKnownMarkdown.isNotEmpty()) {
                    lifecycleScope.launch {
                        clipboardManager().setPrimaryClip(
                            android.content.ClipData.newPlainText("markdown", requestSnapshotBlocking().markdown),
                        )
                        showToast("已复制文件内容")
                    }
                })
                add(host("分享文件", enabled = hasDoc && lastKnownMarkdown.isNotEmpty()) {
                    lifecycleScope.launch {
                        val share = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
                            type = "text/plain"
                            putExtra(android.content.Intent.EXTRA_TEXT, requestSnapshotBlocking().markdown)
                        }
                        startActivity(android.content.Intent.createChooser(share, "分享文件"))
                    }
                })
            }),
            TopMenuSection("编辑", listOf(
                entry("撤销", "undo"),
                entry("重做", "redo"),
                entry("剪切", "cut", separator = true),
                entry("拷贝", "copy"),
                TopMenuEntry("粘贴", true, false) {
                    clipboardManager().primaryClip?.getItemAt(0)?.coerceToText(this)?.toString()
                        ?.takeIf { it.isNotEmpty() }?.let { postCommand("pasteClipboard") }
                },
                entry("全选", "selectAll", separator = true),
            )),
            TopMenuSection("插入", listOf(
                entry("行内公式", "insertMathInline"),
                entry("块级公式", "insertMathBlock"),
                entry("Mermaid 图表", "insertMermaid"),
                entry("水平线", "insertHorizontalRule"),
                entry("插入脚注", "insertFootnote"),
                entry("插入图片…", "insertImage", action = { openImageLauncher.launch("image/*") }),
                entry("插入超链接…", "setLink", separator = true, action = {
                    linkInput = "https://"
                    showLinkDialog.value = true
                }),
                entry("移除链接", "removeLink"),
                entry("YAML 前置元数据", "showFrontMatter", separator = true),
            )),
            TopMenuSection("格式", buildList {
                add(entry("粗体", "toggleBold"))
                add(entry("斜体", "toggleItalic"))
                add(entry("下划线", "toggleUnderline"))
                add(entry("删除线", "toggleStrike"))
                add(entry("高亮", "toggleHighlight"))
                add(entry("行内代码", "toggleCode"))
                add(entry("正文", "setParagraph", separator = true))
                add(entry("一级标题", "setHeading1"))
                add(entry("二级标题", "setHeading2"))
                add(entry("三级标题", "setHeading3"))
                add(entry("四级标题", "setHeading4"))
                add(entry("五级标题", "setHeading5"))
                add(entry("六级标题", "setHeading6"))
                add(entry("无序列表", "toggleBulletList", separator = true))
                add(entry("有序列表", "toggleOrderedList"))
                add(entry("任务列表", "toggleTaskList"))
                add(entry("增加列表缩进", "indentListItem"))
                add(entry("减少列表缩进", "outdentListItem"))
                add(entry("引用", "toggleBlockquote", separator = true))
                add(entry("代码块", "toggleCodeBlock"))
                add(entry("声明代码语言…", "setCodeBlockLanguage", action = { showToolbarLanguageDialog.value = true }))
                add(entry("格式化代码块", "formatCodeBlock"))
                add(entry("提示框：备注", "insertAlertNote", separator = true))
                add(entry("提示框：提示", "insertAlertTip"))
                add(entry("提示框：重要", "insertAlertImportant"))
                add(entry("提示框：警告", "insertAlertWarning"))
                add(entry("提示框：注意", "insertAlertCaution"))
                add(entry("顺时针旋转图片", "rotateImageClockwise", separator = true))
                add(entry("图片 50%", "resizeImage", text = "50"))
                add(entry("图片 75%", "resizeImage", text = "75"))
                add(entry("图片 90%", "resizeImage", text = "90"))
                add(entry("图片 100%", "resizeImage", text = "100"))
                add(entry("格式刷", "formatPainter", separator = true))
                add(entry("应用格式刷", "formatPainterApply"))
                add(entry("清除格式", "clearFormat", separator = true))
            }),
            TopMenuSection("表格", listOf(
                entry("编辑表格标题", "setTableCaption"),
                entry("上方插行", "addRowBefore", separator = true),
                entry("下方插行", "addRowAfter"),
                entry("删除当前行", "deleteRow"),
                entry("左插列", "addColumnBefore", separator = true),
                entry("右插列", "addColumnAfter"),
                entry("删除当前列", "deleteColumn"),
                entry("内容列宽", "fitTableContents", separator = true),
                entry("平均列宽", "distributeTableColumns"),
                entry("恢复自动列宽", "resetTableWidths"),
                entry("左对齐", "alignTableLeft", separator = true),
                entry("居中对齐", "alignTableCenter"),
                entry("右对齐", "alignTableRight"),
                entry("删除表格", "deleteTable", separator = true),
            )),
            TopMenuSection("段落", listOf(
                entry("段前插入行", "insertLineBefore"),
                entry("段后插入行", "insertLineAfter"),
                entry("重复该段", "duplicateParagraph", separator = true),
                entry("删除该段", "deleteParagraph"),
            )),
            TopMenuSection("视图", listOf(
                host("显示/隐藏侧栏", enabled = true) { onToggleSidebar() },
                host("源码模式", enabled = hasDoc, checked = editorSourceMode.value, separator = true) {
                    postCommand("toggleSourceMode")
                },
                host("只读模式", enabled = hasDoc, checked = androidReadOnly.value, separator = true) {
                    toggleReadOnly()
                },
                host("段落专注模式", enabled = hasDoc, separator = true) { postCommand("setEditorFocusMode") },
                host("打字机模式", enabled = hasDoc) { postCommand("setEditorTypewriterMode") },
                host("放大", enabled = hasDoc, separator = true) { applyZoom(zoomPercent.value + 10) },
                host("缩小", enabled = hasDoc) { applyZoom(zoomPercent.value - 10) },
                host("重置 100%", enabled = hasDoc) { applyZoom(100) },
            )),
            TopMenuSection("更多", listOf(
                host("首选项…") { showPreferences.value = true },
                host("关于 MarkLeaf", separator = true) { showAbout.value = true },
            )),
        )
    }

    /** 只读模式切换：前端 setReadOnly 命令（text=1/0），状态用于菜单勾选。 */
    private fun toggleReadOnly() {
        val next = !androidReadOnly.value
        androidReadOnly.value = next
        bridge.post(command("setReadOnly", if (next) "1" else "0"))
        showToast(if (next) "已进入只读模式" else "已退出只读模式")
    }

    /** 新建文档：清生命周期状态并下发空内容（纯文本另发文档类型）。 */
    private fun newDocument(documentType: String) {
        val active = controller ?: DocumentController(source, bridge::post, session).also {
            encodingInfoFromPref(editorPrefs.value.defaultEncoding)?.let { info -> it.setEncoding(info) }
            it.setNewline(editorPrefs.value.defaultNewline)
            controller = it
        }
        active.startNewDocument()
        androidReadOnly.value = false
        if (documentType == "plainText") {
            bridge.post(
                HostMessage.SetDocumentType(documentId = session.documentId, revision = 0),
            )
        }
        bridge.post(
            HostMessage.LoadDocument(
                documentId = session.documentId,
                revision = 0,
                payload = HostMessage.LoadDocument.LoadDocumentPayload(
                    markdown = "",
                    documentType = documentType,
                ),
            ),
        )
        activeTitle.value = "未命名"
        dirty.value = false
        showToast(if (documentType == "plainText") "已新建纯文本" else "已新建 Markdown 文档")
    }

    /** 关闭当前文档：丢弃前写恢复快照（重开应用可找回），回到欢迎文档。 */
    private fun closeDocument() {
        lifecycleScope.launch {
            if (dirty.value && lastKnownMarkdown.isNotEmpty()) {
                runCatching {
                    val markdown = requestSnapshotBlocking().markdown
                    lastKnownMarkdown = markdown
                    val active = controller
                    if (active != null) {
                        active.writeRecovery(recoveryStore, markdown, dirty = true)
                    } else {
                        recoveryStore.writePending(session.documentId, null, markdown)
                    }
                }
            }
            controller?.startNewDocument()
            androidReadOnly.value = false
            bridge.post(welcomeDocumentMessage())
            activeTitle.value = "未命名"
            dirty.value = false
            documentEncoding.value = "UTF-8"
            documentNewline.value = "LF"
            showToast("已关闭当前文档")
        }
    }

    private fun closeWorkspace() {
        workspaceRoot = null
        sidebarEntries.value = emptyList()
        showToast("已关闭工作区")
    }

    /** 最近文件：SharedPreferences 持久化（uri|名称 按行，最多 5 条，新开顶置）。 */
    private fun rememberRecentFile(uri: String, name: String) {
        val current = recentFiles().filter { it.first != uri }.toMutableList()
        current.add(0, uri to name)
        val trimmed = current.take(5)
        prefsStore.edit().putString("recentFiles", trimmed.joinToString("\n") { "${it.first}|${it.second}" }).apply()
    }

    private fun recentFiles(): List<Pair<String, String>> =
        prefsStore.getString("recentFiles", null)
            ?.lineSequence()
            ?.mapNotNull { line ->
                val split = line.indexOf('|')
                if (split <= 0) null else line.substring(0, split) to line.substring(split + 1)
            }
            ?.toList()
            ?: emptyList()

    /** 工作区侧栏长按：重命名当前条目名（SAF rename）。 */
    private fun renameSidebarEntry(entry: DocumentEntry, newName: String) {
        lifecycleScope.launch {
            runCatching { source.rename(entry.ref, newName) }
                .onSuccess { renamed ->
                    refreshWorkspace()
                    if (controller?.activeRef == entry.ref) {
                        controller?.adoptRef(renamed)
                        activeTitle.value = newName
                    }
                    showToast("已重命名")
                }
                .onFailure { showToast("重命名失败：${it.message}") }
        }
    }

    /** 工作区侧栏长按：删除文件/文件夹。 */
    private fun deleteSidebarEntry(entry: DocumentEntry) {
        lifecycleScope.launch {
            runCatching { source.delete(entry.ref) }
                .onSuccess {
                    refreshWorkspace()
                    showToast("已删除")
                }
                .onFailure { showToast("删除失败：${it.message}") }
        }
    }

    /** SAF 图片 → 复制进文档目录 → 以相对路径插入（insertImage 命令）。 */
    private suspend fun insertImageFromSaf(sourceUri: Uri) {
        runCatching {
            val docDir = activeDocumentDirectory()
            val name = "image-" + System.currentTimeMillis() + ".png"
            val bytes = contentResolver.openInputStream(sourceUri)?.use { it.readBytes() }
                ?: throw IllegalStateException("无法读取所选图片")
            val ref = source.createDocument(DocumentRef.Saf(docDir), name)
            source.write(ref, bytes)
            "assets/$name"
        }.onSuccess { relativePath ->
            postCommandWithText("insertImage", relativePath)
        }.onFailure {
            showToast("插入图片失败：${it.message}")
        }
    }

    private fun postCommandWithText(name: String, text: String) {
        bridge.post(command(name, text))
    }

    /** 首选项编码名 → EncodingInfo（与桌面 DocumentEncodingPolicy 同名同序；
     *  桌面 UTF-16 系列均为 LE，"UTF-16 with BOM" = LE 正文 + BOM 头）。 */
    private fun encodingInfoFromPref(name: String): EncodingInfo? = when (name) {
        "UTF-8" -> EncodingInfo("UTF-8", hasBom = false)
        "UTF-8 with BOM" -> EncodingInfo("UTF-8", hasBom = true)
        "UTF-16" -> EncodingInfo("UTF-16LE", hasBom = false)
        "UTF-16 with BOM" -> EncodingInfo("UTF-16LE", hasBom = true)
        "GB18030" -> EncodingInfo("GB18030", hasBom = false)
        "GBK" -> EncodingInfo("GBK", hasBom = false)
        "GB2312" -> EncodingInfo("GB2312", hasBom = false)
        "Big5" -> EncodingInfo("Big5", hasBom = false)
        "Shift_JIS" -> EncodingInfo("Shift_JIS", hasBom = false)
        "US-ASCII" -> EncodingInfo("US-ASCII", hasBom = false)
        else -> null
    }

    /** EncodingInfo → 桌面同款显示名（状态栏/对话框选项匹配用）。 */
    private fun encodingDisplayName(info: EncodingInfo): String = when {
        info.name == "UTF-8" && info.hasBom -> "UTF-8 with BOM"
        info.name == "UTF-16LE" && info.hasBom -> "UTF-16 with BOM"
        info.name == "UTF-16LE" -> "UTF-16"
        else -> info.name
    }

    /** 启动发现未保存快照：取最近一份弹恢复对话框（其余在流程消化后继续弹）。 */
    private fun maybeShowRecoveryDialog() {
        if (recoveryEntry.value != null) return
        val newest = recoveryStore.pendingIds()
            .mapNotNull { recoveryStore.consume(it) }
            .maxByOrNull { it.savedAt } ?: return
        recoveryEntry.value = newest
    }

    /** 恢复快照：SAF 文档先打开再覆盖内容；未命名文档直接载入当前会话。 */
    private fun restoreRecoverySnapshot(entry: RecoveryStore.Entry) {
        recoveryEntry.value = null
        val markdown = entry.markdown
        val ref = entry.ref
        if (ref == null) {
            recoveryStore.discard(entry.documentId)
            bridge.post(
                HostMessage.LoadDocument(
                    documentId = session.documentId,
                    revision = session.confirmedRevision,
                    payload = HostMessage.LoadDocument.LoadDocumentPayload(markdown = markdown),
                ),
            )
            dirty.value = true
            activeTitle.value = "未命名（恢复）"
        } else {
            pendingRecoveryMarkdown = markdown
            pendingRecoveryId = entry.documentId
            openDocument(ref)
        }
    }

    /** 另存副本：快照写入工作区新文档（未打开工作区时不可用）。 */
    private fun saveRecoveryCopy(entry: RecoveryStore.Entry) {
        val folder = workspaceRoot
        if (folder == null) {
            showToast("未打开工作区，无法另存副本")
            return
        }
        lifecycleScope.launch {
            runCatching {
                val stamp = java.text.SimpleDateFormat("yyyyMMdd-HHmmss", java.util.Locale.US)
                    .format(java.util.Date(entry.savedAt))
                val newRef = source.createDocument(folder, "recovered-$stamp.md")
                source.write(newRef, entry.markdown.toByteArray(Charsets.UTF_8))
                newRef
            }.onSuccess { newRef ->
                recoveryStore.discard(entry.documentId)
                recoveryEntry.value = null
                showToast("已另存副本 ${(newRef as? DocumentRef.Saf)?.let { Uri.parse(it.uri).lastPathSegment?.substringAfterLast("%2F") } ?: ""}")
                maybeShowRecoveryDialog()
            }.onFailure {
                showToast("另存副本失败：${it.message}")
            }
        }
    }

    private fun discardRecoverySnapshot(entry: RecoveryStore.Entry) {
        recoveryStore.discard(entry.documentId)
        recoveryEntry.value = null
        maybeShowRecoveryDialog()
    }

    private fun recoveryDisplayName(entry: RecoveryStore.Entry): String =
        entry.ref?.let { ref ->
            Uri.parse(ref.uri).lastPathSegment?.substringAfterLast("%2F")
                ?.substringBeforeLast('.')
                ?.takeIf { it.isNotEmpty() }
        } ?: "未命名文档"

    override fun onPause() {
        super.onPause()
        // spec §4.3：切后台写恢复快照（含未命名文档——无 SAF 引用也进恢复链路）。
        // lastKnownMarkdown 仅在保存/导出时刷新，切后台需现取一份快照；
        // HOME 场景下进程仍存活，挂起请求可在 onPause 返回后完成。
        if (dirty.value) {
            lifecycleScope.launch {
                runCatching {
                    val markdown = requestSnapshotBlocking().markdown
                    lastKnownMarkdown = markdown
                    val active = controller
                    if (active != null) {
                        active.writeRecovery(recoveryStore, markdown, dirty = true)
                    } else {
                        recoveryStore.writePending(session.documentId, null, markdown)
                    }
                }
            }
        }
    }

    /** 压制原生文本选择工具条：浮动 ActionMode 一启动就结束。选区与拖动
     *  手柄由 Chromium 绘制不受影响，工具条由自绘浮层替代。 */
    override fun onActionModeStarted(mode: android.view.ActionMode) {
        if (mode.type == android.view.ActionMode.TYPE_FLOATING) {
            mode.finish()
            return
        }
        super.onActionModeStarted(mode)
    }

    /** 自绘选择工具条：位置 = 前端选区矩形（设备坐标），随拖动手柄实时
     *  跟随；条目按语义上下文组装（系统命令 + 编辑器格式命令）。 */
    private data class SelectionToolbarItem(
        val title: String,
        val enabled: Boolean,
        val action: () -> Unit,
        val separatorBefore: Boolean = false,
    )

    @Composable
    private fun SelectionToolbar(
        rect: androidx.compose.ui.geometry.Rect?,
        visible: Boolean,
        semanticContext: String?,
        requestSelection: suspend () -> String,
        actions: Map<String, Pair<Boolean, Boolean>>,
        onCommand: (String, String) -> Unit,
        onLanguagePicker: () -> Unit,
    ) {
        if (rect == null || !visible) return
        val context = androidx.compose.ui.platform.LocalContext.current
        val clipboard = context.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
        fun asyncCopyThen(action: () -> Unit): () -> Unit = {
            lifecycleScope.launch {
                val text = requestSelection()
                if (text.isNotEmpty()) {
                    clipboard.setPrimaryClip(android.content.ClipData.newPlainText("markdown", text))
                    action()
                }
            }
        }
        // 基础命令（对齐桌面文本选择组）
        val base = listOf(
            SelectionToolbarItem("剪切", actions["cut"]?.first == true, action = asyncCopyThen {
                onCommand("cut", "")
            }),
            SelectionToolbarItem("拷贝", actions["copy"]?.first == true, action = asyncCopyThen {}),
            SelectionToolbarItem("粘贴", actions["paste"]?.first == true, action = {
                clipboard.primaryClip?.getItemAt(0)?.coerceToText(context)?.toString()
                    ?.takeIf { it.isNotEmpty() }?.let { onCommand("pasteClipboard", "") }
            }),
            SelectionToolbarItem("全选", true, action = { onCommand("selectAll", "") }),
        )
        // 上下文定制（对齐桌面 EditorSession+ContextMenu 各语义上下文命令组；
        // 桌面依赖原生面板的条目——更换/另存图片、将图表另存为、表格/图片标题、
        // 更改脚注标识——Android 尚无对应 UI，暂不列出）
        fun item(title: String, command: String, text: String = "", separator: Boolean = false): SelectionToolbarItem =
            SelectionToolbarItem(title, actions[command]?.first == true, { onCommand(command, text) }, separator)
        val contextItems = when (semanticContext) {
            // 桌面 math：编辑公式源码 / 公式编号…（块级）/ 转为行内·块级 ─ 删除
            "math" -> listOf(
                item("编辑公式源码", "editMath"),
                item("公式编号…", "setMathNumber"),
                item("转为行内/块级", "convertMath"),
                item("删除公式", "deleteMath", separator = true),
            )
            // 桌面 mermaid：编辑源码 / 重新渲染 ─ 删除
            "mermaid" -> listOf(
                item("编辑 Mermaid 源码", "editMermaid"),
                item("重新渲染图表", "rerenderMermaid"),
                item("删除图表", "deleteMermaid", separator = true),
            )
            // 桌面 codeBlock：声明语言 / 格式化 / 复制整段 ─ 转为正文
            "codeBlock" -> listOf(
                SelectionToolbarItem("声明代码语言…", true, onLanguagePicker),
                item("格式化代码块", "formatCodeBlock"),
                item("复制整段代码", "copyCodeBlock"),
                item("转为正文", "exitCode", separator = true),
            )
            // 桌面 table：行组 / 列组 / 列宽组 / 对齐组 ─ 删除表格
            "table" -> listOf(
                item("上方插行", "addRowBefore"),
                item("下方插行", "addRowAfter"),
                item("删除当前行", "deleteRow"),
                item("左插列", "addColumnBefore"),
                item("右插列", "addColumnAfter"),
                item("删除当前列", "deleteColumn"),
                item("内容列宽", "fitTableContents"),
                item("平均列宽", "distributeTableColumns"),
                item("恢复自动列宽", "resetTableWidths"),
                item("左对齐", "alignTableLeft"),
                item("居中对齐", "alignTableCenter"),
                item("右对齐", "alignTableRight"),
                item("删除表格", "deleteTable", separator = true),
            )
            // 桌面 image：缩放（子菜单拍平为百分比）/ 旋转
            "image" -> listOf(
                item("50%", "resizeImage", "50"),
                item("75%", "resizeImage", "75"),
                item("90%", "resizeImage", "90"),
                item("100%", "resizeImage", "100"),
                item("顺时针旋转", "rotateImageClockwise"),
            )
            // 桌面 footnoteDefinition：转到引用 / 删除全部引用 / 删除脚注及全部引用
            "footnoteDefinition" -> listOf(
                item("转到引用", "goToFootnoteReference"),
                item("删除全部引用", "clearFootnoteReferences"),
                item("删除脚注及全部引用", "deleteFootnote"),
            )
            // 桌面 frontMatter：复制元数据 ─ 移到元数据之后
            "frontMatter" -> listOf(
                item("复制元数据", "copyCodeBlock"),
                item("移到元数据之后", "exitCode"),
            )
            // 桌面普通文本：NativeFormatTextMenu（格式刷组 + 格式组 + 清除格式）
            else -> listOf(
                item("格式刷", "formatPainter"),
                item("应用格式刷", "formatPainterApply"),
                item("粗体", "toggleBold"),
                item("斜体", "toggleItalic"),
                item("下划线", "toggleUnderline"),
                item("删除线", "toggleStrike"),
                item("高亮", "toggleHighlight"),
                item("行内代码", "toggleCode"),
                item("清除格式", "clearFormat", separator = true),
            )
        }
        val items = base + contextItems
        val density = androidx.compose.ui.platform.LocalDensity.current
        fun px(dpValue: Int): Int = with(density) { dpValue.dp.roundToPx() }
        val marginPx = px(12)
        // 工具条固定横贯屏宽（两侧 12dp 边距），条目超出由内部横向滚动承载；
        // 垂直位置 = 选区上方 56dp
        val offsetYpx = (rect.top.roundToInt() - px(56)).coerceAtLeast(marginPx)
        val offsetYdp = with(density) { offsetYpx.toDp() }
        val horizontalMarginDp = with(density) { marginPx.toDp() }
        androidx.compose.foundation.layout.Box(
            modifier = Modifier
                .offset(y = offsetYdp)
                .fillMaxWidth()
                .padding(horizontal = horizontalMarginDp)
        ) {
            // visibleState 首组 false→true：每次弹出都播放入场动画
            val appearState = androidx.compose.runtime.remember {
                androidx.compose.animation.core.MutableTransitionState(false)
            }.apply { targetState = true }
            androidx.compose.animation.AnimatedVisibility(
                visibleState = appearState,
                enter = androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(140)) +
                    androidx.compose.animation.scaleIn(
                        initialScale = 0.92f,
                        animationSpec = androidx.compose.animation.core.tween(180),
                    ),
            ) {
                androidx.compose.material3.Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = androidx.compose.foundation.shape.RoundedCornerShape(22.dp),
                    shadowElevation = 6.dp,
                    color = MaterialTheme.colorScheme.surfaceContainer,
                ) {
                    androidx.compose.foundation.layout.Row(
                        modifier = Modifier
                            .horizontalScroll(rememberScrollState())
                            .padding(horizontal = 10.dp, vertical = 2.dp),
                        horizontalArrangement = Arrangement.spacedBy(2.dp),
                        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                    ) {
                        items.forEach { item ->
                            Text(
                                item.title,
                                modifier = Modifier
                                    .clickable(enabled = item.enabled, onClick = item.action)
                                    .padding(horizontal = 12.dp, vertical = 8.dp),
                                style = MaterialTheme.typography.labelMedium,
                                maxLines = 1,
                                color = if (item.enabled) MaterialTheme.colorScheme.onSurface
                                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
                            )
                        }
                    }
                }
            }
        }
    }

    companion object {
        private const val TAG = "MainActivity"
        private const val WELCOME_DOCUMENT_ID = "welcome"
    }
}

@Composable
private fun androidx.compose.foundation.layout.ColumnScope.EditorContainer(
    activeWebView: WebView?,
    onPinch: (Float, Boolean) -> Unit,
) {
    androidx.compose.ui.viewinterop.AndroidView(
        factory = { context -> android.widget.FrameLayout(context) },
        update = { frame ->
            // 崩溃重建后 activeWebView 换新实例：容器换载并重挂捏合手势
            if (activeWebView != null &&
                (frame.childCount == 0 || frame.getChildAt(0) !== activeWebView)
            ) {
                frame.removeAllViews()
                frame.addView(
                    activeWebView,
                    android.widget.FrameLayout.LayoutParams(
                        android.view.ViewGroup.LayoutParams.MATCH_PARENT,
                        android.view.ViewGroup.LayoutParams.MATCH_PARENT,
                    ),
                )
                val detector = android.view.ScaleGestureDetector(
                    frame.context,
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
                activeWebView.setOnTouchListener { _, event ->
                    detector.onTouchEvent(event)
                    false
                }
            }
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
                .padding(start = 10.dp)
                .clickable(onClick = onEncoding),
            style = itemStyle, color = itemColor,
        )
        Text(
            text = newline,
            modifier = Modifier
                .padding(start = 10.dp)
                .clickable(onClick = onNewline),
            style = itemStyle, color = itemColor,
        )
        Text(
            text = if (sourceMode) "源码" else "可视化",
            modifier = Modifier
                .padding(start = 10.dp)
                .clickable(onClick = onToggleMode),
            style = itemStyle, color = itemColor,
        )
        Text(
            text = "$zoom%",
            modifier = Modifier
                .padding(start = 10.dp)
                .clickable(onClick = onZoom),
            style = itemStyle, color = itemColor,
        )
        if (characterCount.isNotEmpty()) {
            // 对齐桌面：字数按钮点击弹出文档统计面板
            Text(
                text = "$characterCount 字符",
                modifier = Modifier
                    .padding(start = 10.dp)
                    .clickable(onClick = onShowStatistics),
                style = itemStyle,
                color = itemColor,
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

/** 保存写回编码选项（与桌面 DocumentEncodingPolicy 同名同序同语义：
 *  "UTF-16" 系列均为 LE；BOM 变体仅多文件头）。 */
private val ENCODING_OPTIONS = listOf(
    "UTF-8" to com.markleaf.app.documents.EncodingInfo("UTF-8", hasBom = false),
    "UTF-8 with BOM" to com.markleaf.app.documents.EncodingInfo("UTF-8", hasBom = true),
    "UTF-16" to com.markleaf.app.documents.EncodingInfo("UTF-16LE", hasBom = false),
    "UTF-16 with BOM" to com.markleaf.app.documents.EncodingInfo("UTF-16LE", hasBom = true),
    "GB18030" to com.markleaf.app.documents.EncodingInfo("GB18030", hasBom = false),
    "GBK" to com.markleaf.app.documents.EncodingInfo("GBK", hasBom = false),
    "GB2312" to com.markleaf.app.documents.EncodingInfo("GB2312", hasBom = false),
    "Big5" to com.markleaf.app.documents.EncodingInfo("Big5", hasBom = false),
    "Shift_JIS" to com.markleaf.app.documents.EncodingInfo("Shift_JIS", hasBom = false),
    "US-ASCII" to com.markleaf.app.documents.EncodingInfo("US-ASCII", hasBom = false),
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
