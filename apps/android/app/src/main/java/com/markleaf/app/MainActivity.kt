@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app

import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
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
import androidx.compose.runtime.LaunchedEffect
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
import com.markleaf.app.documents.SafDocumentPath
import com.markleaf.app.documents.isMarkdownName
import com.markleaf.app.documents.parsedUri
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
    private val autoSaveHandler = android.os.Handler(android.os.Looper.getMainLooper())
    private val snapshotHandler = android.os.Handler(android.os.Looper.getMainLooper())
    private val snapshotRunnable: Runnable = Runnable {
        lifecycleScope.launch {
            if (dirty.value) {
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
            // 脏期间按快照间隔循环；转 clean 由 DirtyChanged 取消
            if (dirty.value) {
                snapshotHandler.postDelayed(snapshotRunnable, editorPrefs.value.snapshotInterval * 1000L)
            }
        }
    }
    private val autoSaveRunnable = Runnable {
        if (editorPrefs.value.autoSave && dirty.value && documentLoaded.value &&
            !androidReadOnly.value && controller?.activeRef != null
        ) {
            lifecycleScope.launch { saveActiveDocument() }
        }
    }
    private val showCloseConfirm = mutableStateOf(false)
    private val showLinkDialog = mutableStateOf(false)
    private var linkInput = ""
    private val showImageUrlDialog = mutableStateOf(false)
    private var imageUrlInput = "https://"
    private val showTableSizeDialog = mutableStateOf(false)
    private val tableRowsState = mutableStateOf(3)
    private val tableColsState = mutableStateOf(3)
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
                activeTitle.value = Uri.parse(savedRef.uri).lastPathSegment?.substringAfterLast("%2F") ?: L10n.t("未命名")
                dirty.value = false
                androidReadOnly.value = false
                showToast(L10n.f("已另存为 %s", activeTitle.value))
                rememberRecentFile(savedRef.uri, activeTitle.value)
            }.onFailure { showToast(L10n.f("另存为失败：%s", it.message)) }
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
    private val activeTitle = mutableStateOf(L10n.t("未命名"))
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
    private val editorReadyState = mutableStateOf(false)
    /** 渲染进程崩溃保留的内容：ready 握手后重放，避免回落到欢迎文档。 */
    private var pendingCrashRestoreMarkdown: String? = null

    private val openTreeLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree(),
    ) { uri ->
        if (uri != null) {
SafDocumentSource.takePersistablePermission(this, uri)
            rememberRecentFolder(uri.toString(), treeDisplayName(uri))
            workspaceRoot = DocumentRef.Saf(uri.toString())
            refreshWorkspace()
        }
    }

    private val openFileLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument(),
    ) { uri ->
        if (uri != null) {
            val readOnly = pendingReadOnlyOpen
            pendingReadOnlyOpen = false
            openDocument(DocumentRef.Saf(uri.toString()), readOnly = readOnly)
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
                showToast(L10n.t("HTML 已导出"))
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // targetSdk 35+ 强制 edge-to-edge：内容绘制到系统栏下。系统栏图标
        // 对比度由 enableEdgeToEdge 按系统深色模式自动处理；顶栏/底栏经
        // statusBarsPadding/navigationBarsPadding 让出系统栏区域。
        enableEdgeToEdge()

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
        L10n.language = editorPrefs.value.displayLanguage
        activeTitle.value = L10n.t("未命名")
        // 对齐桌面 WindowSession：关闭「打开时还原上次的缩放比例」则会话从 100% 起步
        zoomPercent.value = if (editorPrefs.value.restoreZoomOnOpen) editorPrefs.value.zoom else 100
        bridge = EditorBridge(
            postScript = { script -> webView.evaluateJavascript(script, null) },
            session = session,
        )
        bridge.attach(webView)

        setContent {
            // 壳层与编辑区文档都跟随系统外观；用户显式选择的颜色主题仍保持不变。
            val systemDark = androidx.compose.foundation.isSystemInDarkTheme()
            LaunchedEffect(systemDark, editorReadyState.value) {
                if (editorReadyState.value) applyStyles()
            }
            MaterialTheme(
                colorScheme = if (systemDark) androidx.compose.material3.darkColorScheme()
                else androidx.compose.material3.lightColorScheme(),
            ) {
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
                                prefs = editorPrefs.value,
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
                                        showToast(L10n.f("保存时将使用 %s 编码", documentEncoding.value))
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
                                    title = { Text(L10n.t("声明代码语言")) },
                                    text = {
                                        // 预设列表与 macOS CodeBlockLanguageCatalog 对齐；
                                        // 输入框保持可编辑（列表只是便利项，非白名单）。
                                        @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
                                        Column {
                                            androidx.compose.material3.OutlinedTextField(
                                                value = languageInput.value,
                                                onValueChange = { languageInput.value = it },
                                                label = { Text(L10n.t("语言")) },
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
                                        }) { Text(L10n.t("确定")) }
                                    },
                                    dismissButton = {
                                        TextButton(onClick = { showLanguageDialog.value = null }) { Text(L10n.t("取消")) }
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
                    title = { Text(L10n.t("声明代码语言")) },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = toolbarLanguageInput,
                            onValueChange = { toolbarLanguageInput = it },
                            label = { Text(L10n.t("语言")) },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            bridge.post(command("setCodeBlockLanguage", toolbarLanguageInput))
                            showToolbarLanguageDialog.value = false
                        }) { Text(L10n.t("确定")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showToolbarLanguageDialog.value = false }) { Text(L10n.t("取消")) }
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
                    colorThemes = StyleAssets.colorThemes(this@MainActivity),
                    onChange = ::updatePrefs,
                    onClearHistory = ::clearRecentHistory,
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
            // 插入来自互联网的图片：URL 输入（CSP 允许 https 图片直连渲染）
            if (showImageUrlDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showImageUrlDialog.value = false },
                    title = { Text(L10n.t("插入来自互联网的图片")) },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = imageUrlInput,
                            onValueChange = { imageUrlInput = it },
                            label = { Text(L10n.t("图片 URL")) },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            val url = imageUrlInput.trim()
                            if (url.isNotBlank()) postCommand("insertImage", "$url\n图片")
                            showImageUrlDialog.value = false
                        }) { Text(L10n.t("插入")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showImageUrlDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 插入表格：独立对话框指定行列
            if (showTableSizeDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showTableSizeDialog.value = false },
                    title = { Text(L10n.t("插入表格")) },
                    text = {
                        Column {
                            androidx.compose.foundation.layout.Row(
                                modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
                                verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                            ) {
                                Text(L10n.t("行数"), modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
                                TextButton(onClick = { tableRowsState.value = (tableRowsState.value - 1).coerceAtLeast(1) }) { Text("−") }
                                Text(
                                    "${tableRowsState.value}",
                                    modifier = Modifier.padding(horizontal = 12.dp),
                                    style = MaterialTheme.typography.bodyLarge,
                                )
                                TextButton(onClick = { tableRowsState.value = (tableRowsState.value + 1).coerceAtMost(20) }) { Text("+") }
                            }
                            androidx.compose.foundation.layout.Row(
                                modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
                                verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                            ) {
                                Text(L10n.t("列数"), modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
                                TextButton(onClick = { tableColsState.value = (tableColsState.value - 1).coerceAtLeast(1) }) { Text("−") }
                                Text(
                                    "${tableColsState.value}",
                                    modifier = Modifier.padding(horizontal = 12.dp),
                                    style = MaterialTheme.typography.bodyLarge,
                                )
                                TextButton(onClick = { tableColsState.value = (tableColsState.value + 1).coerceAtMost(20) }) { Text("+") }
                            }
                            Text(
                                L10n.t("带表头行；行列范围 1–20"),
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            postCommand("insertTable", "${tableRowsState.value},${tableColsState.value}")
                            showTableSizeDialog.value = false
                        }) { Text(L10n.t("插入")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showTableSizeDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 关闭当前文档：脏状态先给保存/丢弃选择
            if (showCloseConfirm.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showCloseConfirm.value = false },
                    title = { Text(L10n.t("关闭前保存修改？")) },
                    text = { Text(L10n.t("当前文档有未保存的修改。")) },
                    confirmButton = {
                        TextButton(onClick = {
                            showCloseConfirm.value = false
                            lifecycleScope.launch {
                                saveActiveDocument()
                                closeDocument()
                            }
                        }) { Text(L10n.t("保存并关闭")) }
                    },
                    dismissButton = {
                        androidx.compose.foundation.layout.Row {
                            TextButton(onClick = {
                                showCloseConfirm.value = false
                                closeDocument()
                            }) { Text(L10n.t("不保存")) }
                            TextButton(onClick = { showCloseConfirm.value = false }) { Text(L10n.t("取消")) }
                        }
                    },
                )
            }
            // 插入超链接：URL 输入（作用于当前选区）
            if (showLinkDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showLinkDialog.value = false },
                    title = { Text(L10n.t("插入超链接")) },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = linkInput,
                            onValueChange = { linkInput = it },
                            label = { Text(L10n.t("链接地址")) },
                            singleLine = true,
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            if (linkInput.isNotBlank()) postCommand("setLink", linkInput.trim())
                            showLinkDialog.value = false
                        }) { Text(L10n.t("确定")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showLinkDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 重命名当前文档（SAF rename）
            if (renameDocDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { renameDocDialog.value = false },
                    title = { Text(L10n.t("重命名")) },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = renameDocInput,
                            onValueChange = { renameDocInput = it },
                            label = { Text(L10n.t("新名称")) },
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
                                            showToast(L10n.t("已重命名"))
                                        }
                                        .onFailure { showToast(L10n.f("重命名失败：%s", it.message)) }
                                }
                            }
                        }) { Text(L10n.t("确定")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { renameDocDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 删除当前文档确认
            if (deleteDocConfirm.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { deleteDocConfirm.value = false },
                    title = { Text(L10n.t("删除当前文档？")) },
                    text = { Text(L10n.t("将从存储中删除该文件，此操作不可撤销。")) },
                    confirmButton = {
                        TextButton(onClick = {
                            deleteDocConfirm.value = false
                            val ref = controller?.activeRef
                            if (ref != null) {
                                lifecycleScope.launch {
                                    runCatching { source.delete(ref) }
                                        .onSuccess {
                                            showToast(L10n.t("已删除"))
                                            closeDocument()
                                        }
                                        .onFailure { showToast(L10n.f("删除失败：%s", it.message)) }
                                }
                            }
                        }) { Text(L10n.t("删除")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { deleteDocConfirm.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 侧栏条目重命名（长按）
            renameEntryDialog.value?.let { entry ->
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { renameEntryDialog.value = null },
                    title = { Text(L10n.f("重命名「%s」", entry.name)) },
                    text = {
                        androidx.compose.material3.OutlinedTextField(
                            value = renameEntryInput,
                            onValueChange = { renameEntryInput = it },
                            label = { Text(L10n.t("新名称")) },
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
                        }) { Text(L10n.t("确定")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { renameEntryDialog.value = null }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 启动恢复：最近一份未保存快照（其余在消化后继续弹出）
            recoveryEntry.value?.let { entry ->
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { /* 必须明确选择：恢复 / 另存副本 / 丢弃 */ },
                    title = { Text(L10n.t("发现未保存的修改")) },
                    text = {
                        Text(
                            L10n.f(
                                "文档「%1${'$'}s」在 %2${'$'}s 有未保存的内容。",
                                recoveryDisplayName(entry),
                                java.text.DateFormat.getDateTimeInstance()
                                    .format(java.util.Date(entry.savedAt)),
                            ),
                        )
                    },
                    confirmButton = {
                        TextButton(onClick = { restoreRecoverySnapshot(entry) }) { Text(L10n.t("恢复")) }
                    },
                    dismissButton = {
                        androidx.compose.foundation.layout.Row {
                            TextButton(onClick = { saveRecoveryCopy(entry) }) { Text(L10n.t("另存副本")) }
                            TextButton(onClick = { discardRecoverySnapshot(entry) }) { Text(L10n.t("丢弃")) }
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
        SafDocumentPath.parentDocumentUri(Uri.parse(ref.uri))?.toString() ?: ref.uri

    /** 渲染进程崩溃恢复：销毁重建 WebView 并重置会话（spec §8）。 */
    private fun handleRenderProcessGone(goneWebView: WebView): Boolean {
        Log.e(TAG, "WebView 渲染进程崩溃，重建")
        editorReadyState.value = false
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

    private fun openDocument(ref: DocumentRef.Saf, readOnly: Boolean = false) {
        // 对齐桌面 SaveOnDocumentSwitch（默认开）：打开另一文件前保存当前修改
        // （仅已有 SAF 引用且非只读；未命名/只读文档无写回目标或不适用）
        if (editorPrefs.value.saveOnSwitch && dirty.value &&
            controller?.activeRef != null && !androidReadOnly.value
        ) {
            lifecycleScope.launch { saveActiveDocument() }
        }
        val active = controller ?: DocumentController(source, bridge::post, session).also {
            // 新控制器落首选项的默认编码/换行；打开已有文件时会被探测值覆盖
            encodingInfoFromPref(editorPrefs.value.defaultEncoding)?.let(it::setEncoding)
            it.setNewline(editorPrefs.value.defaultNewline)
            controller = it
        }
        lifecycleScope.launch {
            when (val result = active.open(ref, readOnly = readOnly)) {
                is DocumentController.OpenResult.Loaded -> {
                    activeTitle.value = result.name
                    dirty.value = false
                    androidReadOnly.value = readOnly
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
                        activeTitle.value =
                            result.name.substringBeforeLast('.') + L10n.t("（恢复）")
                        showToast(L10n.t("已恢复未保存的修改"))
                    }
                }
                DocumentController.OpenResult.PermissionLost -> {
                    showToast(L10n.t("文档权限失效，请重新打开"))
                    // 恢复流程被打断：重新弹对话框让用户重选
                    if (pendingRecoveryMarkdown != null) {
                        pendingRecoveryMarkdown = null
                        pendingRecoveryId = null
                        maybeShowRecoveryDialog()
                    }
                }
                is DocumentController.OpenResult.Failed -> {
                    showToast(L10n.f("打开失败: %s", result.message))
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
                showToast(L10n.t("已保存"))
            }
            DocumentController.SaveResult.ReadOnly -> showToast(L10n.t("只读文档不能保存，请使用另存为"))
            DocumentController.SaveResult.Conflict -> showToast(L10n.t("文件已被外部修改，请另存或覆盖"))
            DocumentController.SaveResult.PermissionLost -> showToast(L10n.t("保存失败：权限失效"))
            DocumentController.SaveResult.NotDirty -> Unit
            is DocumentController.SaveResult.Failed -> showToast(L10n.f("保存失败: %s", result.message))
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
                editorReadyState.value = true
                welcomeDocumentId = frontendId
                Log.i(TAG, "editor ready; adopting documentId=$frontendId")
                // 主题样式：baseCss 按序拼接 base → 颜色主题 → serif，保证
                // --ml-*/--bg-* 变量与排版规则完整且覆盖顺序正确。
                lastAppliedStyleId = editorPrefs.value.styleId
                val colorThemeId = effectiveColorThemeId()
                bridge.post(
                    HostMessage.ApplyStyles(
                        documentId = frontendId,
                        revision = 0,
                        payload = HostMessage.ApplyStyles.ApplyStylesPayload(
                            baseCss = StyleAssets.readBaseCss(this) + "\n" +
                                StyleAssets.readColorThemeCss(this, colorThemeId) + "\n" +
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
                applyEditorKernelSettings()
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
                showToast(L10n.t("代码已复制"))
            }
            is EditorMessage.CodeBlockLanguageRequested -> {
                // 点击语言标签会顺带聚焦 WebView 编辑区弹出键盘，盖住对话框，先收起
                dismissKeyboard()
                showLanguageDialog.value = Pair(message.payload.position, message.payload.language)
            }
            is EditorMessage.CodeFormatRequested -> {
                // Android 第一版无外部代码格式化器：明示不可用（不静默吞）。
                dismissKeyboard()
                showToast(L10n.t("未配置代码格式化器"))
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
                    }.onFailure { showToast(L10n.t("无法打开链接")) }
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
            is EditorMessage.DirtyChanged -> {
                dirty.value = message.payload.dirty
                // 自动保存（对齐桌面 FileSettings.AutoSave，默认关）：脏起 3s 防抖；
                // 仅对已有 SAF 引用且非只读的文档生效（未命名文档无写回目标）
                autoSaveHandler.removeCallbacks(autoSaveRunnable)
                snapshotHandler.removeCallbacks(snapshotRunnable)
                if (message.payload.dirty) {
                    autoSaveHandler.postDelayed(autoSaveRunnable, 3000)
                    // 周期性恢复快照（对齐桌面快照保存间隔，默认 30s）
                    snapshotHandler.postDelayed(snapshotRunnable, editorPrefs.value.snapshotInterval * 1000L)
                }
            }
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
            "\"sourceFontFamily\":'${prefs.sourceFontFamily}'," +
            "\"cjkLanguage\":'${prefs.cjkLanguage}'," +
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

    /** 应用缩放：捏合/菜单共用；更新缩放值、持久化到首选项（供「打开时还原」）并下发外观。 */
    private fun applyZoom(percent: Int) {
        val clamped = percent.coerceIn(50, 200)
        zoomPercent.value = clamped
        if (editorPrefs.value.zoom != clamped) {
            editorPrefs.value = editorPrefs.value.copy(zoom = clamped)
            editorPrefs.value.save(prefsStore)
        }
        applyAppearance()
    }

    /** 首选项变更：保存 + 立即应用（缩放字段变化才同步 zoomPercent，避免关闭
     *  「打开时还原」后改任意首选项把会话缩放拉回存档值；主题相关变更重发样式载荷）。 */
    private fun updatePrefs(newPrefs: com.markleaf.app.ui.EditorPrefs) {
        val old = editorPrefs.value
        editorPrefs.value = newPrefs
        newPrefs.save(prefsStore)
        if (newPrefs.zoom != old.zoom) zoomPercent.value = newPrefs.zoom
        if (newPrefs.styleId != old.styleId || newPrefs.colorTheme != old.colorTheme ||
            newPrefs.followSystemTheme != old.followSystemTheme ||
            newPrefs.defaultLightTheme != old.defaultLightTheme ||
            newPrefs.defaultDarkTheme != old.defaultDarkTheme
        ) {
            lastAppliedStyleId = newPrefs.styleId
            applyStyles()
        }
        if (documentLoaded.value) {
            if (newPrefs.codeHighlight != old.codeHighlight) {
                postCommand("setCodeHighlightVisible", if (newPrefs.codeHighlight) "1" else "0")
            }
            if (newPrefs.blockHandle != old.blockHandle) {
                postCommand("setBlockHandleVisible", if (newPrefs.blockHandle) "1" else "0")
            }
            if (newPrefs.sourceIndent != old.sourceIndent) {
                postCommand("setSourceIndent", newPrefs.sourceIndent.toString())
            }
            if (newPrefs.displayLanguage != old.displayLanguage) {
                L10n.language = newPrefs.displayLanguage
                postCommand("setLanguage", newPrefs.displayLanguage)
            }
            if (newPrefs.codeFence != old.codeFence || newPrefs.emphasisMarker != old.emphasisMarker ||
                newPrefs.bulletMarker != old.bulletMarker ||
                newPrefs.exitBlockOnEmptyEnter != old.exitBlockOnEmptyEnter ||
                newPrefs.useShiftEnterHardBreak != old.useShiftEnterHardBreak ||
                newPrefs.escapeLiteralSymbols != old.escapeLiteralSymbols ||
                newPrefs.escapeMarkdownLiteralSymbols != old.escapeMarkdownLiteralSymbols ||
                newPrefs.codeBlockSpellcheck != old.codeBlockSpellcheck
            ) {
                applyEditorKernelSettings()
            }
        }
        applyAppearance()
    }

    /** 编辑器内核设置初始/变更应用：代码高亮、段落块手柄、源码缩进、
     *  编辑器语言（frontend 命令通道；崩溃重建后 DocumentLoaded 重放时一并恢复）。 */
    private fun applyEditorKernelSettings() {
        val prefs = editorPrefs.value
        bridge.post(command("setCodeHighlightVisible", if (prefs.codeHighlight) "1" else "0"))
        bridge.post(command("setBlockHandleVisible", if (prefs.blockHandle) "1" else "0"))
        bridge.post(command("setSourceIndent", prefs.sourceIndent.toString()))
        bridge.post(command("setLanguage", prefs.displayLanguage))
        // 字段集合与桌面 EditorSession.applyMarkdownEditingSettings 相同
        val markdownSettings = "{" +
            "\"exitBlockOnEmptyEnter\":${prefs.exitBlockOnEmptyEnter}," +
            "\"useShiftEnterHardBreak\":${prefs.useShiftEnterHardBreak}," +
            "\"codeFence\":\"${prefs.codeFence}\"," +
            "\"emphasisMarker\":\"${prefs.emphasisMarker}\"," +
            "\"bulletMarker\":\"${prefs.bulletMarker}\"," +
            "\"escapeLiteralSymbols\":${prefs.escapeLiteralSymbols}," +
            "\"escapeMarkdownLiteralSymbols\":${prefs.escapeMarkdownLiteralSymbols}," +
            "\"codeBlockSpellcheck\":${prefs.codeBlockSpellcheck}}"
        bridge.post(command("setMarkdownEditingSettings", markdownSettings))
    }

    /** 下发主题样式载荷（base + 当前颜色主题 + 排版样式目录）。 */
    private fun applyStyles() {
        val colorThemeId = effectiveColorThemeId()
        bridge.post(
            HostMessage.ApplyStyles(
                documentId = session.documentId,
                revision = session.confirmedRevision,
                payload = HostMessage.ApplyStyles.ApplyStylesPayload(
                    baseCss = StyleAssets.readBaseCss(this) + "\n" +
                        StyleAssets.readColorThemeCss(this, colorThemeId) + "\n" +
                        StyleAssets.readTypographyCss(this, editorPrefs.value.styleId),
                    styles = styleCatalogJson(),
                    activeStyle = editorPrefs.value.styleId,
                ),
            ),
        )
    }

    /** 生效颜色主题（对齐桌面 EditorSession.applyStyles：开启「与操作系统同步」时
     *  按系统外观取默认浅/深主题，否则用已选主题；缺失项逐级回退）。 */
    private fun effectiveColorThemeId(): String {
        val systemDark = (resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) ==
            android.content.res.Configuration.UI_MODE_NIGHT_YES
        val prefs = editorPrefs.value
        return StyleAssets.resolveColorThemeId(
            selected = prefs.colorTheme,
            followSystem = prefs.followSystemTheme,
            systemDark = systemDark,
            preferredLight = prefs.defaultLightTheme,
            preferredDark = prefs.defaultDarkTheme,
            catalog = StyleAssets.colorThemes(this),
        )
    }

    /** 双指捏合：factor 为本次增量；finished 表示手势结束（补齐被限流的末次应用）。
     *  首选项关闭「使用双指捏合进行缩放」时忽略手势。 */
    private fun onPinchZoom(factor: Float, finished: Boolean) {
        if (!editorPrefs.value.pinchZoom) return
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
            children: List<TopMenuEntry>? = null,
        ) = TopMenuEntry(title, en(cmd), separator, ck(cmd), children) {
            if (action != null) action() else postCommand(cmd, text ?: "")
        }
        fun host(
            title: String,
            enabled: Boolean = true,
            separator: Boolean = false,
            checked: Boolean? = null,
            children: List<TopMenuEntry>? = null,
            action: () -> Unit = {},
        ) = TopMenuEntry(title, enabled, separator, checked, children, action)
        fun sub(title: String, enabled: Boolean = true, separator: Boolean = false, children: List<TopMenuEntry>) =
            TopMenuEntry(title, enabled, separator, children = children)
        fun cmdEntry(title: String, cmd: String, separator: Boolean = false, text: String? = null) =
            TopMenuEntry(title, en(cmd), separator) { postCommand(cmd, text ?: "") }
        val hasDoc = documentLoaded.value
        val hasFile = controller?.activeRef != null
        val recent = recentFiles()
        return listOf(
            TopMenuSection(L10n.t("文件"), buildList {
                add(sub(L10n.t("新建"), children = listOf(
                    host(L10n.t("Markdown 文档")) { newDocument("markdown") },
                    host(L10n.t("纯文本")) { newDocument("plainText") },
                )))
                add(host(L10n.t("打开文件夹…"), separator = true) { openTreeLauncher.launch(null) })
                add(host(L10n.t("打开文件…")) { pendingReadOnlyOpen = false; openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) })
                add(host(L10n.t("以只读方式打开…")) { pendingReadOnlyOpen = true; openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) })
                if (recent.isNotEmpty()) {
                    add(sub(L10n.t("最近文件"), separator = true, children = recent.map { recentEntry ->
                        host(recentEntry.second) { openDocument(DocumentRef.Saf(recentEntry.first)) }
                    }))
                }
                val recentFolderList = recentFolders()
                if (recentFolderList.isNotEmpty()) {
                    add(sub(L10n.t("最近文件夹"), children = recentFolderList.map { recentEntry ->
                        host(recentEntry.second) { reopenRecentFolder(recentEntry.first) }
                    }))
                }
                add(host(L10n.t("恢复未保存的文件…"), separator = true, enabled = recoveryStore.pendingIds().isNotEmpty()) {
                    maybeShowRecoveryDialog()
                    if (recoveryEntry.value == null) showToast(L10n.t("没有待恢复的内容"))
                })
                add(host(L10n.t("保存"), separator = true, enabled = hasDoc && !androidReadOnly.value) { lifecycleScope.launch { saveActiveDocument() } })
                add(host(L10n.t("另存为…"), enabled = hasDoc) { saveAsLauncher.launch(L10n.t("未命名.md")) })
                add(host(L10n.t("导出…"), enabled = hasDoc) { showExport.value = true })
                add(host(L10n.t("关闭当前文档"), enabled = hasDoc, separator = true) {
                    if (dirty.value) showCloseConfirm.value = true else closeDocument()
                })
                add(host(L10n.t("关闭工作区"), enabled = workspaceRoot != null) { closeWorkspace() })
                add(host(L10n.t("重命名…"), enabled = hasFile, separator = true) {
                    renameDocInput = activeTitle.value
                    renameDocDialog.value = true
                })
                add(host(L10n.t("删除"), enabled = hasFile) { deleteDocConfirm.value = true })
                add(host(L10n.t("复制文件路径"), enabled = hasFile, separator = true) {
                    controller?.activeRef?.let {
                        clipboardManager().setPrimaryClip(android.content.ClipData.newPlainText("path", it.uri))
                        showToast(L10n.t("已复制文件 URI"))
                    }
                })
                add(host(L10n.t("复制文件内容"), enabled = hasDoc && lastKnownMarkdown.isNotEmpty()) {
                    lifecycleScope.launch {
                        clipboardManager().setPrimaryClip(
                            android.content.ClipData.newPlainText("markdown", requestSnapshotBlocking().markdown),
                        )
                        showToast(L10n.t("已复制文件内容"))
                    }
                })
                add(host(L10n.t("分享文件"), enabled = hasDoc && lastKnownMarkdown.isNotEmpty()) {
                    lifecycleScope.launch {
                        val share = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
                            type = "text/plain"
                            putExtra(android.content.Intent.EXTRA_TEXT, requestSnapshotBlocking().markdown)
                        }
                        startActivity(android.content.Intent.createChooser(share, L10n.t("分享文件")))
                    }
                })
            }),
            TopMenuSection(L10n.t("编辑"), listOf(
                entry(L10n.t("撤销"), "undo"),
                entry(L10n.t("重做"), "redo"),
                entry(L10n.t("剪切"), "cut", separator = true),
                entry(L10n.t("拷贝"), "copy"),
                TopMenuEntry(L10n.t("粘贴"), true, false) {
                    clipboardManager().primaryClip?.getItemAt(0)?.coerceToText(this)?.toString()
                        ?.takeIf { it.isNotEmpty() }?.let { postCommand("pasteClipboard") }
                },
                entry(L10n.t("全选"), "selectAll", separator = true),
            )),
            TopMenuSection(L10n.t("插入"), listOf(
                sub(L10n.t("图片"), enabled = hasDoc, children = listOf(
                    host(L10n.t("插入本地图片…")) { openImageLauncher.launch("image/*") },
                    host(L10n.t("插入剪贴板图片…"), enabled = clipboardImageUri() != null) {
                        clipboardImageUri()?.let { imageUri ->
                            lifecycleScope.launch { insertImageFromSaf(imageUri) }
                        }
                    },
                    host(L10n.t("插入来自互联网的图片…")) {
                        imageUrlInput = "https://"
                        showImageUrlDialog.value = true
                    },
                )),
                sub(L10n.t("表格"), enabled = en("insertTable"), children = listOf(
                    host(L10n.t("插入表格（指定行列）…")) { showTableSizeDialog.value = true },
                )),
                entry(L10n.t("Mermaid 图表"), "insertMermaid"),
                entry(L10n.t("插入超链接…"), "setLink", action = {
                    linkInput = "https://"
                    showLinkDialog.value = true
                }),
                entry(L10n.t("插入脚注"), "insertFootnote"),
                entry(L10n.t("行内公式"), "insertMathInline", separator = true),
                entry(L10n.t("块级公式"), "insertMathBlock"),
                entry(L10n.t("水平线"), "insertHorizontalRule"),
                entry(L10n.t("YAML 前置元数据"), "showFrontMatter", separator = true),
            )),
            TopMenuSection(L10n.t("格式"), buildList {
                add(entry(L10n.t("粗体"), "toggleBold"))
                add(entry(L10n.t("斜体"), "toggleItalic"))
                add(entry(L10n.t("下划线"), "toggleUnderline"))
                add(entry(L10n.t("删除线"), "toggleStrike"))
                add(entry(L10n.t("高亮"), "toggleHighlight"))
                add(entry(L10n.t("行内代码"), "toggleCode"))
                add(sub(L10n.t("段落样式"), separator = true, children = listOf(
                    cmdEntry(L10n.t("正文"), "setParagraph"),
                    cmdEntry(L10n.t("一级标题"), "setHeading1"),
                    cmdEntry(L10n.t("二级标题"), "setHeading2"),
                    cmdEntry(L10n.t("三级标题"), "setHeading3"),
                    cmdEntry(L10n.t("四级标题"), "setHeading4"),
                    cmdEntry(L10n.t("五级标题"), "setHeading5"),
                    cmdEntry(L10n.t("六级标题"), "setHeading6"),
                )))
                add(sub(L10n.t("列表"), children = listOf(
                    cmdEntry(L10n.t("无序列表"), "toggleBulletList"),
                    cmdEntry(L10n.t("有序列表"), "toggleOrderedList"),
                    cmdEntry(L10n.t("任务列表"), "toggleTaskList"),
                    cmdEntry(L10n.t("增加列表缩进"), "indentListItem", separator = true),
                    cmdEntry(L10n.t("减少列表缩进"), "outdentListItem"),
                )))
                add(entry(L10n.t("引用"), "toggleBlockquote"))
                add(entry(L10n.t("代码块"), "toggleCodeBlock"))
                add(sub(L10n.t("提示框"), children = listOf(
                    cmdEntry(L10n.t("备注"), "insertAlertNote"),
                    cmdEntry(L10n.t("提示"), "insertAlertTip"),
                    cmdEntry(L10n.t("重要"), "insertAlertImportant"),
                    cmdEntry(L10n.t("警告"), "insertAlertWarning"),
                    cmdEntry(L10n.t("注意"), "insertAlertCaution"),
                )))
                add(sub(L10n.t("表格"), enabled = en("addRowAfter"), separator = true, children = listOf(
                    cmdEntry(L10n.t("编辑表格标题"), "setTableCaption"),
                    cmdEntry(L10n.t("上方插行"), "addRowBefore"),
                    cmdEntry(L10n.t("下方插行"), "addRowAfter"),
                    cmdEntry(L10n.t("删除当前行"), "deleteRow"),
                    cmdEntry(L10n.t("左插列"), "addColumnBefore"),
                    cmdEntry(L10n.t("右插列"), "addColumnAfter"),
                    cmdEntry(L10n.t("删除当前列"), "deleteColumn"),
                    cmdEntry(L10n.t("左对齐"), "alignTableLeft"),
                    cmdEntry(L10n.t("居中对齐"), "alignTableCenter"),
                    cmdEntry(L10n.t("右对齐"), "alignTableRight"),
                    cmdEntry(L10n.t("删除表格"), "deleteTable", separator = true),
                )))
                add(sub(L10n.t("图片"), enabled = en("rotateImageClockwise"), separator = true, children = listOf(
                    cmdEntry(L10n.t("顺时针旋转 90°"), "rotateImageClockwise"),
                    cmdEntry(L10n.t("缩放 50%"), "resizeImage", text = "50"),
                    cmdEntry(L10n.t("缩放 75%"), "resizeImage", text = "75"),
                    cmdEntry(L10n.t("缩放 90%"), "resizeImage", text = "90"),
                    cmdEntry(L10n.t("缩放 100%"), "resizeImage", text = "100"),
                )))
                add(sub(L10n.t("段落操作"), separator = true, children = listOf(
                    cmdEntry(L10n.t("段前插入行"), "insertLineBefore"),
                    cmdEntry(L10n.t("段后插入行"), "insertLineAfter"),
                    cmdEntry(L10n.t("重复该段"), "duplicateParagraph"),
                    cmdEntry(L10n.t("删除该段"), "deleteParagraph"),
                )))
                add(entry(L10n.t("格式刷"), "formatPainter", separator = true))
                add(entry(L10n.t("应用格式刷"), "formatPainterApply"))
                add(entry(L10n.t("格式化代码块"), "formatCodeBlock"))
                add(entry(L10n.t("清除格式"), "clearFormat", separator = true))
            }),
            TopMenuSection(L10n.t("视图"), listOf(
                host(L10n.t("显示/隐藏侧栏")) { onToggleSidebar() },
                host(L10n.t("源码模式"), enabled = hasDoc, checked = editorSourceMode.value, separator = true) { postCommand("toggleSourceMode") },
                host(L10n.t("只读模式"), enabled = hasDoc, checked = androidReadOnly.value, separator = true) {
                    toggleReadOnly()
                },
                host(L10n.t("段落专注模式"), enabled = hasDoc, separator = true) { postCommand("setEditorFocusMode") },
                host(L10n.t("打字机模式"), enabled = hasDoc) { postCommand("setEditorTypewriterMode") },
                host(L10n.t("放大"), enabled = hasDoc, separator = true) { applyZoom(zoomPercent.value + 10) },
                host(L10n.t("缩小"), enabled = hasDoc) { applyZoom(zoomPercent.value - 10) },
                host(L10n.t("重置 100%"), enabled = hasDoc) { applyZoom(100) },
            )),
            TopMenuSection(L10n.t("更多"), listOf(
                host(L10n.t("首选项…")) { showPreferences.value = true },
                host(L10n.t("关于 MarkLeaf"), separator = true) { showAbout.value = true },
            )),
        )
    }

    /** 只读模式切换：前端 setReadOnly 命令（text=1/0），状态用于菜单勾选。 */
    private fun toggleReadOnly() {
        val next = !androidReadOnly.value
        androidReadOnly.value = next
        // 控制器同源：菜单开关后的保存放行/拦截跟随会话实际只读态
        controller?.setReadOnly(next)
        bridge.post(command("setReadOnly", if (next) "1" else "0"))
        showToast(if (next) L10n.t("已进入只读模式") else L10n.t("已退出只读模式"))
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
        activeTitle.value = L10n.t("未命名")
        dirty.value = false
        showToast(if (documentType == "plainText") L10n.t("已新建纯文本") else L10n.t("已新建 Markdown 文档"))
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
            activeTitle.value = L10n.t("未命名")
            dirty.value = false
            documentEncoding.value = "UTF-8"
            documentNewline.value = "LF"
            showToast(L10n.t("已关闭当前文档"))
        }
    }

    private fun closeWorkspace() {
        workspaceRoot = null
        sidebarEntries.value = emptyList()
        showToast(L10n.t("已关闭工作区"))
    }

    /** 最近文件：SharedPreferences 持久化（uri|名称 按行，最多 5 条，新开顶置）。
     *  记录开关关闭时不新增（对齐桌面 recordRecentFiles 语义：仅停记录，旧项仍显示）。 */
    private fun rememberRecentFile(uri: String, name: String) {
        if (!editorPrefs.value.recordRecent) return
        val current = recentFiles().filter { it.first != uri }.toMutableList()
        current.add(0, uri to name)
        val trimmed = current.take(5)
        prefsStore.edit().putString("recentFiles", trimmed.joinToString("\n") { "${it.first}|${it.second}" }).apply()
    }

    /** 记录最近文件夹（对齐桌面 recordRecentFolders；tree URI 已持持久化权限）。 */
    private fun rememberRecentFolder(uri: String, name: String) {
        if (!editorPrefs.value.recordRecentFolders) return
        val current = recentFolders().filter { it.first != uri }.toMutableList()
        current.add(0, uri to name)
        val trimmed = current.take(5)
        prefsStore.edit().putString("recentFolders", trimmed.joinToString("\n") { "${it.first}|${it.second}" }).apply()
    }

    private fun clearRecentHistory() {
        // 对齐桌面确认文案：同时删除最近的文件和文件夹记录
        prefsStore.edit().remove("recentFiles").remove("recentFolders").apply()
        showToast(L10n.t("已清除历史记录"))
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

    private fun recentFolders(): List<Pair<String, String>> =
        prefsStore.getString("recentFolders", null)
            ?.lineSequence()
            ?.mapNotNull { line ->
                val split = line.indexOf('|')
                if (split <= 0) null else line.substring(0, split) to line.substring(split + 1)
            }
            ?.toList()
            ?: emptyList()

    /** tree URI → 显示名：tree document id 冒号后的目录段（primary:Docs → Docs）。 */
    private fun treeDisplayName(uri: Uri): String =
        runCatching { android.provider.DocumentsContract.getTreeDocumentId(uri) }
            .getOrNull()?.substringAfter(':', uri.toString())?.takeIf { it.isNotBlank() }
            ?: uri.lastPathSegment ?: uri.toString()

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
                    showToast(L10n.t("已重命名"))
                }
                .onFailure { showToast(L10n.f("重命名失败：%s", it.message)) }
        }
    }

    /** 工作区侧栏长按：删除文件/文件夹。 */
    private fun deleteSidebarEntry(entry: DocumentEntry) {
        lifecycleScope.launch {
            runCatching { source.delete(entry.ref) }
                .onSuccess {
                    refreshWorkspace()
                    showToast(L10n.t("已删除"))
                }
                .onFailure { showToast(L10n.f("删除失败：%s", it.message)) }
        }
    }

    /** SAF 图片 → 复制进文档目录下的 assets/ 子目录（无则创建）→ 以相对路径插入
     *  （insertImage 命令）。对齐桌面 copyImageToAssets + markdownReferencePath：
     *  保留原文件名（同名由 SAF 去重，以实际落盘名为准）、空格编码为 %20、
     *  按首选项决定是否加 "./" 前缀。
     *  回归护栏：曾把文件建在文档目录根下却引用 assets/<name>，且 MIME 硬编码为
     *  text/markdown 导致落盘成 x.png.md——两处都让插入的图片显示不出来。 */
    private suspend fun insertImageFromSaf(sourceUri: Uri) {
        runCatching {
            val docDirUri = activeDocumentDirectory()
            if (docDirUri.isEmpty()) throw IllegalStateException(L10n.t("文档未保存，无法插入图片"))
            val docDir = DocumentRef.Saf(docDirUri)
            val bytes = contentResolver.openInputStream(sourceUri)?.use { it.readBytes() }
                ?: throw IllegalStateException(L10n.t("无法读取所选图片"))
            val sourceName = queryDisplayName(sourceUri)
                ?: "image-${System.currentTimeMillis()}.${imageExtensionOf(sourceUri)}"
            val assetsDir = source.listChildren(docDir)
                .firstOrNull { it.isFolder && it.name.equals("assets", ignoreCase = true) }
                ?.ref ?: source.createFolder(docDir, "assets")
            val created = source.createDocument(assetsDir, sourceName)
            source.write(created, bytes)
            val finalName = (created as? DocumentRef.Saf)?.let { queryDisplayName(it.parsedUri) } ?: sourceName
            val relative = "assets/$finalName".replace(" ", "%20")
            if (editorPrefs.value.prefixRelativeWithDotSlash) "./$relative" else relative
        }.onSuccess { relativePath ->
            postCommandWithText("insertImage", relativePath)
        }.onFailure {
            showToast(L10n.f("插入图片失败：%s", it.message))
        }
    }

    private fun queryDisplayName(uri: Uri): String? = runCatching {
        contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)
            ?.use { cursor -> if (cursor.moveToFirst()) cursor.getString(0) else null }
    }.getOrNull()?.takeIf { it.isNotBlank() }

    /** 剪贴板中的图片 URI（相册「复制图片」等来源）；无图片返回 null。 */
    private fun clipboardImageUri(): Uri? {
        val clip = clipboardManager().primaryClip ?: return null
        val mimeTypes = clip.description.filterMimeTypes("*/*")
        if (mimeTypes.none { it.startsWith("image/") }) return null
        for (index in 0 until clip.itemCount) {
            clip.getItemAt(index).uri?.let { return it }
        }
        return null
    }

    /** 重新打开最近文件夹：tree URI 的持久化权限仍在（takePersistablePermission 已记录）。 */
    private fun reopenRecentFolder(uri: String) {
        val parsed = runCatching { Uri.parse(uri) }.getOrNull() ?: return
        val persisted = contentResolver.persistedUriPermissions.any {
            it.uri == parsed && it.isReadPermission
        }
        if (!persisted) {
            showToast(L10n.t("权限已失效，请重新打开文件夹"))
            return
        }
        workspaceRoot = DocumentRef.Saf(uri)
        refreshWorkspace()
    }

    private fun imageExtensionOf(uri: Uri): String =
        contentResolver.getType(uri)
            ?.let { android.webkit.MimeTypeMap.getSingleton().getExtensionFromMimeType(it) }
            ?: "png"

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
            activeTitle.value = L10n.t("未命名（恢复）")
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
            showToast(L10n.t("未打开工作区，无法另存副本"))
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
                showToast(L10n.f("已另存副本 %s", (newRef as? DocumentRef.Saf)?.let { Uri.parse(it.uri).lastPathSegment?.substringAfterLast("%2F") } ?: ""))
                maybeShowRecoveryDialog()
            }.onFailure {
                showToast(L10n.f("另存副本失败：%s", it.message))
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
        } ?: L10n.t("未命名文档")

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
            SelectionToolbarItem(L10n.t("剪切"), actions["cut"]?.first == true, action = asyncCopyThen {
                onCommand("cut", "")
            }),
            SelectionToolbarItem(L10n.t("拷贝"), actions["copy"]?.first == true, action = asyncCopyThen {}),
            SelectionToolbarItem(L10n.t("粘贴"), actions["paste"]?.first == true, action = {
                clipboard.primaryClip?.getItemAt(0)?.coerceToText(context)?.toString()
                    ?.takeIf { it.isNotEmpty() }?.let { onCommand("pasteClipboard", "") }
            }),
            SelectionToolbarItem(L10n.t("全选"), true, action = { onCommand("selectAll", "") }),
        )
        // 上下文定制（对齐桌面 EditorSession+ContextMenu 各语义上下文命令组；
        // 桌面依赖原生面板的条目——更换/另存图片、将图表另存为、表格/图片标题、
        // 更改脚注标识——Android 尚无对应 UI，暂不列出）
        fun item(title: String, command: String, text: String = "", separator: Boolean = false): SelectionToolbarItem =
            SelectionToolbarItem(title, actions[command]?.first == true, { onCommand(command, text) }, separator)
        val contextItems = when (semanticContext) {
            // 桌面 math：编辑公式源码 / 公式编号…（块级）/ 转为行内·块级 ─ 删除
            "math" -> listOf(
                item(L10n.t("编辑公式源码"), "editMath"),
                item(L10n.t("公式编号…"), "setMathNumber"),
                item(L10n.t("转为行内/块级"), "convertMath"),
                item(L10n.t("删除公式"), "deleteMath", separator = true),
            )
            // 桌面 mermaid：编辑源码 / 重新渲染 ─ 删除
            "mermaid" -> listOf(
                item(L10n.t("编辑 Mermaid 源码"), "editMermaid"),
                item(L10n.t("重新渲染图表"), "rerenderMermaid"),
                item(L10n.t("删除图表"), "deleteMermaid", separator = true),
            )
            // 桌面 codeBlock：声明语言 / 格式化 / 复制整段 ─ 转为正文
            "codeBlock" -> listOf(
                SelectionToolbarItem(L10n.t("声明代码语言…"), true, onLanguagePicker),
                item(L10n.t("格式化代码块"), "formatCodeBlock"),
                item(L10n.t("复制整段代码"), "copyCodeBlock"),
                item(L10n.t("转为正文"), "exitCode", separator = true),
            )
            // 桌面 table：行组 / 列组 / 列宽组 / 对齐组 ─ 删除表格
            "table" -> listOf(
                item(L10n.t("上方插行"), "addRowBefore"),
                item(L10n.t("下方插行"), "addRowAfter"),
                item(L10n.t("删除当前行"), "deleteRow"),
                item(L10n.t("左插列"), "addColumnBefore"),
                item(L10n.t("右插列"), "addColumnAfter"),
                item(L10n.t("删除当前列"), "deleteColumn"),
                item(L10n.t("内容列宽"), "fitTableContents"),
                item(L10n.t("平均列宽"), "distributeTableColumns"),
                item(L10n.t("恢复自动列宽"), "resetTableWidths"),
                item(L10n.t("左对齐"), "alignTableLeft"),
                item(L10n.t("居中对齐"), "alignTableCenter"),
                item(L10n.t("右对齐"), "alignTableRight"),
                item(L10n.t("删除表格"), "deleteTable", separator = true),
            )
            // 桌面 image：缩放（子菜单拍平为百分比）/ 旋转
            "image" -> listOf(
                item("50%", "resizeImage", "50"),
                item("75%", "resizeImage", "75"),
                item("90%", "resizeImage", "90"),
                item("100%", "resizeImage", "100"),
                item(L10n.t("顺时针旋转"), "rotateImageClockwise"),
            )
            // 桌面 footnoteDefinition：转到引用 / 删除全部引用 / 删除脚注及全部引用
            "footnoteDefinition" -> listOf(
                item(L10n.t("转到引用"), "goToFootnoteReference"),
                item(L10n.t("删除全部引用"), "clearFootnoteReferences"),
                item(L10n.t("删除脚注及全部引用"), "deleteFootnote"),
            )
            // 桌面 frontMatter：复制元数据 ─ 移到元数据之后
            "frontMatter" -> listOf(
                item(L10n.t("复制元数据"), "copyCodeBlock"),
                item(L10n.t("移到元数据之后"), "exitCode"),
            )
            // 桌面普通文本：NativeFormatTextMenu（格式刷组 + 格式组 + 清除格式）
            else -> listOf(
                item(L10n.t("格式刷"), "formatPainter"),
                item(L10n.t("应用格式刷"), "formatPainterApply"),
                item(L10n.t("粗体"), "toggleBold"),
                item(L10n.t("斜体"), "toggleItalic"),
                item(L10n.t("下划线"), "toggleUnderline"),
                item(L10n.t("删除线"), "toggleStrike"),
                item(L10n.t("高亮"), "toggleHighlight"),
                item(L10n.t("行内代码"), "toggleCode"),
                item(L10n.t("清除格式"), "clearFormat", separator = true),
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
    prefs: com.markleaf.app.ui.EditorPrefs,
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
    // 各字段可见性对齐桌面「自定义状态栏」（侧栏切换/命令状态为桌面专属字段）
    val leading = listOfNotNull(
        if (prefs.statusBarPosition && line.isNotEmpty()) L10n.f("行 %1${'$'}s 列 %2${'$'}s", line, column) else null,
        if (prefs.statusBarBlockType) blockType.ifEmpty { null } else null,
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
        if (prefs.statusBarEncoding) {
            Text(
                text = encoding,
                modifier = Modifier
                    .padding(start = 10.dp)
                    .clickable(onClick = onEncoding),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarNewline) {
            Text(
                text = newline,
                modifier = Modifier
                    .padding(start = 10.dp)
                    .clickable(onClick = onNewline),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarModeToggle) {
            Text(
                text = if (sourceMode) L10n.t("源码") else L10n.t("可视化"),
                modifier = Modifier
                    .padding(start = 10.dp)
                    .clickable(onClick = onToggleMode),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarZoom) {
            Text(
                text = "$zoom%",
                modifier = Modifier
                    .padding(start = 10.dp)
                    .clickable(onClick = onZoom),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarWordCount && characterCount.isNotEmpty()) {
            // 对齐桌面：字数按钮点击弹出文档统计面板
            Text(
                text = "$characterCount ${L10n.t("字符")}",
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
    "paragraph" -> L10n.t("正文")
    "heading1" -> L10n.t("标题 1")
    "heading2" -> L10n.t("标题 2")
    "heading3" -> L10n.t("标题 3")
    "heading4" -> L10n.t("标题 4")
    "heading5" -> L10n.t("标题 5")
    "heading6" -> L10n.t("标题 6")
    "blockquote" -> L10n.t("引用")
    "codeBlock" -> L10n.t("代码块")
    "bulletList" -> L10n.t("无序列表")
    "orderedList" -> L10n.t("有序列表")
    "taskList" -> L10n.t("任务列表")
    "table" -> L10n.t("表格")
    "image" -> L10n.t("图片")
    "footnoteDefinition" -> L10n.t("脚注")
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
        confirmButton = { TextButton(onClick = onDismiss) { Text(L10n.t("关闭")) } },
        title = { Text(L10n.t("保存编码")) },
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
        confirmButton = { TextButton(onClick = onDismiss) { Text(L10n.t("关闭")) } },
        title = { Text(L10n.t("换行符")) },
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
        confirmButton = { TextButton(onClick = onDismiss) { Text(L10n.t("关闭")) } },
        title = { Text(L10n.t("缩放")) },
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
        L10n.t("字符数") to field("characterCount"),
        L10n.t("已选字符数") to field("selectedCharacterCount"),
        L10n.t("总字符数") to field("totalCharacterCount"),
        L10n.t("非空白字符数") to field("nonWhitespaceCharacterCount"),
        L10n.t("CJK 字符数") to field("cjkCharacterCount"),
        L10n.t("西文单词数") to field("westernWordCount"),
        L10n.t("公式数") to field("formulaCount"),
        L10n.t("代码行数") to field("codeLineCount"),
        L10n.t("段落数") to field("paragraphCount"),
        L10n.t("行") to field("line"),
        L10n.t("列") to field("column"),
        L10n.t("块类型") to blockTypeDisplayName(field("blockType")),
        L10n.t("编码") to encoding,
        L10n.t("换行符") to newline,
    )
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(L10n.t("关闭")) }
        },
        title = { Text(L10n.t("文档统计")) },
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
