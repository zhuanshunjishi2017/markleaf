@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app

import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.KeyEvent
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.horizontalScroll
import androidx.compose.ui.focus.focusRequester
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.background
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.Row
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.shape.RoundedCornerShape
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
import com.markleaf.app.documents.StartupDocumentPolicy
import com.markleaf.app.documents.isMarkdownName
import com.markleaf.app.documents.parsedUri
import com.markleaf.app.editor.EditorWebViewFactory
import com.markleaf.app.editor.StyleAssets
import com.markleaf.app.export.ExportController
import com.markleaf.app.export.ExportFormat
import com.markleaf.app.export.ExportDialog
import com.markleaf.app.export.ExportOptions
import com.markleaf.app.export.ExportScreen
import com.markleaf.app.export.PdfExporter
import com.markleaf.app.export.loadExportDocument
import com.markleaf.app.export.evaluateExportScript
import com.markleaf.app.ui.OutlineItem
import com.markleaf.app.ui.ThemeSelectionPolicy
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
    // 工作区开合的 Compose 镜像：workspaceRoot 为普通 var，组合内读不到
    // 变更（空目录时 sidebarEntries 不变、无法触发重组），侧栏空态文案
    // 需要它区分「未打开」与「无受支持文件」。
    private val workspaceOpen = mutableStateOf(false)

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
    // 「恢复未保存的文档」列表弹窗（对齐桌面 RecoveryWindowController）：
    // 文件菜单入口打开，展示全部快照供逐条选择。
    private val recoveryList = mutableStateOf<List<RecoveryStore.Entry>>(emptyList())
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
    // 侧栏新建文件但未打开工作区：对齐桌面（桌面侧栏无工作区时只提供
    // 「打开文件夹」入口），弹窗引导用户先打开工作区文件夹。
    private val showNeedWorkspaceDialog = mutableStateOf(false)
    // 侧栏 ＋ 新建文件（已开工作区，对齐桌面 createWorkspaceFile）：名称
    // 对话框 → 建到当前文档所在文件夹/工作区根 → 打开并重扫。
    private val showNewFileDialog = mutableStateOf(false)
    private var newFileFolderRef: DocumentRef.Saf? = null
    private val newFileNameInput = mutableStateOf(
        androidx.compose.ui.text.input.TextFieldValue(""),
    )
    private var newFileKind = "markdown"
    // 侧栏条目长按：对齐桌面右键菜单（bottom sheet 承载，分组一致）
    private val contextEntry = mutableStateOf<com.markleaf.app.documents.DocumentEntry?>(null)
    private val showNewFolderDialog = mutableStateOf(false)
    private var newFolderRef: DocumentRef.Saf? = null
    private val newFolderNameInput = mutableStateOf(
        androidx.compose.ui.text.input.TextFieldValue(""),
    )
    private val deleteEntryConfirm = mutableStateOf<com.markleaf.app.documents.DocumentEntry?>(null)
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
    private val blockMenuVisible = mutableStateOf(false)
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
    // 选区导出的完整三格式内容（text=纯文本、markdown、html）——复制为子菜单按需取用
    private var lastSelectionExport: Triple<String, String, String>? = null
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
    // 查找与替换面板状态（命令驱动的内核查找：findText/findNext/replaceOne/…）
    private val showFindBar = mutableStateOf(false)
    private val showFindReplace = mutableStateOf(false)
    private val findQuery = mutableStateOf("")
    private val findReplaceText = mutableStateOf("")
    private val findCaseSensitive = mutableStateOf(false)
    private val findWholeWord = mutableStateOf(false)
    private val findCurrentCount = mutableStateOf(0)
    private val findTotalCount = mutableStateOf(0)
    private val findFocusRequester = androidx.compose.ui.focus.FocusRequester()
    private var welcomeDocumentId = WELCOME_DOCUMENT_ID
    private var lastKnownMarkdown = ""
    private var pendingSnapshot: kotlinx.coroutines.CompletableDeferred<EditorMessage.Snapshot.SnapshotPayload>? = null
    private val pendingExportHtml = mutableMapOf<String, kotlinx.coroutines.CompletableDeferred<String>>()
    private var pendingHtmlExport: String? = null
    private var pendingPdfExport: java.io.File? = null
    private var rebuiltWebView: WebView? = null
    private lateinit var resolver: AssetResolver
    /** 编辑容器实际挂载的 WebView（崩溃重建后切换，EditorContainer 的 update 跟随）。 */
    private val activeWebViewState = mutableStateOf<WebView?>(null)
    private val editorReadyState = mutableStateOf(false)
    /** 渲染进程崩溃保留的内容：ready 握手后重放，避免回落到欢迎文档。 */
    private var pendingCrashRestoreMarkdown: String? = null
    // Activity recreation state. The recovery store remains the durable source
    // of truth; this state makes rotation/process recreation restore the same
    // document immediately when Android can provide a saved instance bundle.
    private var pendingActivityRestoreMarkdown: String? = null
    private var pendingActivityRestoreRef: DocumentRef.Saf? = null
    private var pendingActivityRestoreDocumentId: String? = null
    private var pendingActivityRestoreTitle: String? = null
    private var pendingActivityRestoreDirty = false

    private val openTreeLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree(),
    ) { uri ->
        if (uri != null) {
SafDocumentSource.takePersistablePermission(this, uri)
            rememberRecentFolder(uri.toString(), treeDisplayName(uri))
            workspaceRoot = DocumentRef.Saf(uri.toString())
            workspaceOpen.value = true
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
        val html = pendingHtmlExport
        pendingHtmlExport = null
        if (uri != null && html != null) lifecycleScope.launch {
            try {
                kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                    checkNotNull(contentResolver.openOutputStream(uri)).use { it.write(html.toByteArray(Charsets.UTF_8)) }
                }
                showToast(L10n.t("HTML 已导出"))
            } catch (e: Exception) { showToast("导出失败：${e.message}") }
        }
    }

    private val createPdfLauncher = registerForActivityResult(
        ActivityResultContracts.CreateDocument("application/pdf"),
    ) { uri ->
        val file = pendingPdfExport
        pendingPdfExport = null
        if (file != null) lifecycleScope.launch {
            try {
                if (uri != null) {
                    kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                        checkNotNull(contentResolver.openOutputStream(uri)).use { output ->
                            file.inputStream().use { it.copyTo(output) }
                        }
                    }
                    showToast("PDF 已导出")
                }
            } catch (e: Exception) { showToast("导出失败：${e.message}") }
            finally { file.delete() }
        }
    }

    @OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        pendingActivityRestoreMarkdown = savedInstanceState?.getString(STATE_MARKDOWN)
        pendingActivityRestoreRef = savedInstanceState?.getString(STATE_REF_URI)
            ?.let { DocumentRef.Saf(it) }
        pendingActivityRestoreDocumentId = savedInstanceState?.getString(STATE_DOCUMENT_ID)
        pendingActivityRestoreTitle = savedInstanceState?.getString(STATE_TITLE)
        pendingActivityRestoreDirty = savedInstanceState?.getBoolean(STATE_DIRTY, false) == true

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
                // uiMode 由当前 Activity 接管，系统栏图标也需要随外观变化更新。
                enableEdgeToEdge()
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
                                        // 源码模式的标题定位以标题文本为锚
                                        // （scrollToSourceHeading，对齐桌面契约）：
                                        // 可视化大纲的 position 是 ProseMirror
                                        // 偏移，比源码字符偏移小，直接当源码
                                        // 位置用会固定落在标题上方几个段落。
                                        if (editorSourceMode.value) {
                                            bridge.post(
                                                command(
                                                    "scrollToSourceHeading",
                                                    "${item.position}\t${item.text}",
                                                ),
                                            )
                                        } else {
                                            bridge.post(command("scrollToPosition", item.position.toString()))
                                        }
                                    },
                                    onCreateDocument = {
                                        scope.launch { drawerState.close() }
                                        if (workspaceRoot == null) {
                                            showNeedWorkspaceDialog.value = true
                                        } else {
                                            promptNewWorkspaceFile()
                                        }
                                    },
                                    onRefresh = { refreshWorkspace() },
                                    onEntryLongPress = { entry ->
                                        contextEntry.value = entry
                                    },
                                    hasWorkspace = workspaceOpen.value,
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
                                quickActions = buildQuickActions(
                                    onSave = { lifecycleScope.launch { saveActiveDocument() } },
                                    onUndo = { postCommand("undo") },
                                    onRedo = { postCommand("redo") },
                                ),
                            )
                            androidx.compose.animation.AnimatedVisibility(
                                visible = showFindBar.value,
                                enter = androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(110)) +
                                    androidx.compose.animation.expandVertically(androidx.compose.animation.core.tween(110)),
                                exit = androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(110)) +
                                    androidx.compose.animation.shrinkVertically(androidx.compose.animation.core.tween(110)),
                            ) {
                                FindReplaceBar()
                            }
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
                        // 全屏导出页：显示时覆盖菜单栏/顶部栏/状态栏与编辑区（后绘者在上）
                        androidx.compose.animation.AnimatedVisibility(
                            visible = showExport.value,
                            enter = androidx.compose.animation.slideInHorizontally(
                                animationSpec = androidx.compose.animation.core.tween(220),
                                initialOffsetX = { it },
                            ) + androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(180)),
                            exit = androidx.compose.animation.slideOutHorizontally(
                                animationSpec = androidx.compose.animation.core.tween(200),
                                targetOffsetX = { it },
                            ) + androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(160)),
                        ) {
                            ExportScreen(
                                defaultName = activeTitle.value,
                                initialStyleId = editorPrefs.value.styleId,
                                initialColorThemeId = effectiveColorThemeId(),
                                styleIds = com.markleaf.app.editor.StyleAssets.TYPOGRAPHY_STYLES,
                                colorThemes = StyleAssets.colorThemes(this@MainActivity),
                                previewHtmlProvider = ::prepareExportHtml,
                                createExportWebView = ::createExportWebView,
                                onSystemPdfPreview = ::openSystemPdfPreview,
                                onDismiss = { showExport.value = false },
                                onExport = { name, options, html, pdf ->
                                    showExport.value = false
                                    performExport(name, options, html, pdf)
                                },
                            )
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
            if (blockMenuVisible.value) {
                BlockMenuSheet(
                    semanticContext = editorSemanticContext.value,
                    actions = editorActions.value,
                    onDismiss = { dismissBlockMenu() },
                    onCommand = { command, text ->
                        dismissBlockMenu()
                        postCommand(command, text ?: "")
                    },
                    onLanguagePicker = {
                        dismissBlockMenu()
                        showToolbarLanguageDialog.value = true
                    },
                )
            }
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
                    activeColorThemeId = effectiveColorThemeId(),
                    onChange = ::updatePrefs,
                    onClearHistory = ::clearRecentHistory,
                    onOpenRecoveryList = {
                        // 打开「恢复未保存的文档」列表：首选项层保持打开，
                        // 列表弹窗以覆盖层形式叠在其上（关闭后回到首选项）。
                        recoveryList.value = recoveryStore.pendingIds()
                            .mapNotNull { recoveryStore.consume(it) }
                            .sortedByDescending { it.savedAt }
                        if (recoveryList.value.isEmpty()) showToast(L10n.t("没有待恢复的内容"))
                    },
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
            // 侧栏新建文件但未打开工作区：引导先打开工作区（对齐桌面侧栏）
            if (showNeedWorkspaceDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showNeedWorkspaceDialog.value = false },
                    title = { Text(L10n.t("请先打开工作区")) },
                    text = { Text(L10n.t("新建文件需要先打开一个工作区文件夹。")) },
                    confirmButton = {
                        TextButton(onClick = {
                            showNeedWorkspaceDialog.value = false
                            openTreeLauncher.launch(null)
                        }) { Text(L10n.t("打开文件夹…")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showNeedWorkspaceDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 侧栏 ＋ 新建文件名称对话框（对齐桌面：预填建议名并全选）
            if (showNewFileDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showNewFileDialog.value = false },
                    title = { Text(L10n.t("新建文件")) },
                    text = {
                        Column {
                            Text(L10n.t("输入文件名："))
                            androidx.compose.material3.OutlinedTextField(
                                value = newFileNameInput.value,
                                onValueChange = { newFileNameInput.value = it },
                                singleLine = true,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(top = 8.dp),
                            )
                        }
                    },
                    confirmButton = {
                        TextButton(onClick = { createWorkspaceFileFromDialog() }) { Text(L10n.t("确定")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showNewFileDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 侧栏条目长按菜单（对齐桌面右键菜单分组；bottom sheet 承载）
            contextEntry.value?.let { entry ->
                androidx.compose.material3.ModalBottomSheet(
                    onDismissRequest = { contextEntry.value = null },
                ) {
                    Row(
                        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 4.dp),
                    ) {
                        Text(
                            text = if (entry.isFolder) "📁" else "📄",
                            style = MaterialTheme.typography.titleLarge,
                            modifier = Modifier.padding(end = 12.dp),
                        )
                        Text(
                            text = entry.name,
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                        )
                    }
                    if (entry.isFolder) {
                        SheetMenuRow(L10n.t("Markdown 文件")) {
                            contextEntry.value = null
                            promptNewWorkspaceFile(folder = entry.ref as? DocumentRef.Saf, kind = "markdown")
                        }
                        SheetMenuRow(L10n.t("文本文件")) {
                            contextEntry.value = null
                            promptNewWorkspaceFile(folder = entry.ref as? DocumentRef.Saf, kind = "plainText")
                        }
                        SheetMenuRow(L10n.t("新建文件夹")) {
                            contextEntry.value = null
                            promptNewWorkspaceFolder(folder = entry.ref as? DocumentRef.Saf)
                        }
                        SheetDivider()
                        SheetMenuRow(L10n.t("复制文件夹路径")) {
                            contextEntry.value = null
                            copyEntryPath(entry)
                        }
                        SheetDivider()
                        SheetMenuRow(L10n.t("重命名")) {
                            contextEntry.value = null
                            renameEntryInput = entry.name
                            renameEntryDialog.value = entry
                        }
                        SheetMenuRow(L10n.t("删除"), destructive = true) {
                            contextEntry.value = null
                            deleteEntryConfirm.value = entry
                        }
                    } else {
                        SheetMenuRow(L10n.t("打开")) {
                            contextEntry.value = null
                            (entry.ref as? DocumentRef.Saf)?.let(::openDocument)
                        }
                        SheetDivider()
                        SheetMenuRow(L10n.t("复制文件路径")) {
                            contextEntry.value = null
                            copyEntryPath(entry)
                        }
                        val copyable = androidx.compose.runtime.produceState(false, entry.name + entry.ref) {
                            val ext = entry.name.substringAfterLast('.', "").lowercase()
                            value = ext in setOf("md", "markdown", "txt") && entryHasCopyableContent(entry)
                        }
                        SheetMenuRow(
                            L10n.t("复制文件内容"),
                            enabled = copyable.value,
                        ) {
                            contextEntry.value = null
                            copyEntryContent(entry)
                        }
                        SheetMenuRow(L10n.t("分享")) {
                            contextEntry.value = null
                            shareEntry(entry)
                        }
                        SheetDivider()
                        SheetMenuRow(L10n.t("重命名")) {
                            contextEntry.value = null
                            renameEntryInput = entry.name
                            renameEntryDialog.value = entry
                        }
                        SheetMenuRow(L10n.t("删除"), destructive = true) {
                            contextEntry.value = null
                            deleteEntryConfirm.value = entry
                        }
                    }
                }
            }
            // 侧栏新建文件夹对话框（对齐桌面 createWorkspaceFolder：预填避让名）
            if (showNewFolderDialog.value) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { showNewFolderDialog.value = false },
                    title = { Text(L10n.t("新建文件夹")) },
                    text = {
                        Column {
                            Text(L10n.t("输入新名称："))
                            androidx.compose.material3.OutlinedTextField(
                                value = newFolderNameInput.value,
                                onValueChange = { newFolderNameInput.value = it },
                                singleLine = true,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(top = 8.dp),
                            )
                        }
                    },
                    confirmButton = {
                        TextButton(onClick = { createWorkspaceFolderFromDialog() }) { Text(L10n.t("确定")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { showNewFolderDialog.value = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 侧栏条目删除确认（对齐桌面 deleteEntry：不可恢复，红色 destructive）
            deleteEntryConfirm.value?.let { entry ->
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { deleteEntryConfirm.value = null },
                    title = { Text(L10n.f("删除「%s」？", entry.name)) },
                    text = { Text(L10n.t("将从存储中删除该文件，此操作不可撤销。")) },
                    confirmButton = {
                        TextButton(onClick = {
                            deleteEntryConfirm.value = null
                            val target = entry
                            lifecycleScope.launch {
                                runCatching { source.delete(target.ref) }
                                    .onSuccess {
                                        if (controller?.activeRef == target.ref) {
                                            closeDocument()
                                        }
                                        refreshWorkspace()
                                        showToast(L10n.t("已删除"))
                                    }
                                    .onFailure { showToast(L10n.f("删除失败：%s", it.message)) }
                            }
                        }) { Text(L10n.t("删除")) }
                    },
                    dismissButton = {
                        TextButton(onClick = { deleteEntryConfirm.value = null }) { Text(L10n.t("取消")) }
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
            // 「恢复未保存的文档」列表弹窗（对齐桌面 RecoveryWindowController：
            // 介绍行 + 名称/时间/原路径列表 + 打开/另存副本/丢弃/全部丢弃/取消）
            if (recoveryList.value.isNotEmpty()) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { recoveryList.value = emptyList() },
                    properties = androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth = false),
                    title = { Text(L10n.t("恢复未保存的文档")) },
                    text = {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth(0.94f)
                                .heightIn(max = 480.dp)
                                .verticalScroll(rememberScrollState()),
                        ) {
                            Text(
                                L10n.f("检测到 %s 个未保存的文档。请选择要恢复的快照：", recoveryList.value.size.toString()),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.padding(bottom = 8.dp),
                            )
                            recoveryList.value.forEach { entry ->
                                androidx.compose.material3.HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                                Row(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .clickable {
                                            recoveryList.value = emptyList()
                                            restoreRecoverySnapshot(entry)
                                        }
                                        .padding(vertical = 10.dp),
                                    verticalAlignment = Alignment.CenterVertically,
                                ) {
                                    Column(modifier = Modifier.weight(1f)) {
                                        Text(
                                            recoveryDisplayName(entry),
                                            style = MaterialTheme.typography.bodyLarge,
                                            fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                                        )
                                        Text(
                                            java.text.DateFormat.getDateTimeInstance()
                                                .format(java.util.Date(entry.savedAt)),
                                            style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        )
                                    }
                                }
                            }
                        }
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            recoveryList.value.forEach { recoveryStore.discard(it.documentId) }
                            recoveryList.value = emptyList()
                            refreshWorkspace()
                            showToast(L10n.t("已删除"))
                        }) {
                            Text(L10n.t("全部丢弃"), color = MaterialTheme.colorScheme.error)
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { recoveryList.value = emptyList() }) { Text(L10n.t("取消")) }
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

    /** 顶栏快捷操作/文件菜单「分享」：以文本形式分享当前文档内容。 */
    private fun shareActiveDocument() {
        lifecycleScope.launch {
            val share = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
                type = "text/plain"
                putExtra(android.content.Intent.EXTRA_TEXT, requestSnapshotBlocking().markdown)
            }
            startActivity(android.content.Intent.createChooser(share, activeTitle.value))
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

    private fun createExportWebView(context: android.content.Context): WebView =
        EditorWebViewFactory.create(context, assetResolver = resolver, documentDir = this::activeDocumentDirectory)

    private suspend fun prepareExportHtml(name: String, options: ExportOptions): String {
        val theme = if (options.printFriendly) StyleAssets.FALLBACK_LIGHT_THEME else options.colorThemeId
        var css = if (theme.isBlank()) "" else StyleAssets.readColorThemeCss(this, theme)
        // Android PDF 白边修复（对齐桌面 PDFGenerator.injectPageMargins）：
        // Chromium Android 打印不传播 body 背景到 @page 区域，深色主题下页边距
        // 保持纸白、与正文黑底割裂。注入 @page{background-color} + html{background}
        // （字面 hex，避免打印管线解析自定义属性失败）；打印友好浅色主题不注入。
        if (!options.printFriendly && css.isNotEmpty()) {
            val bg = Regex("--bg-primary\\s*:\\s*(#[0-9a-fA-F]{3,8})").find(css)?.groupValues?.get(1)
            if (bg != null) {
                val margin = com.markleaf.app.export.MARGIN_PRESETS
                    .firstOrNull { it.id == options.marginPresetId }
                    ?: com.markleaf.app.export.MARGIN_PRESETS.first()
                // 纯 CSS 追加（不带 <style> 标签——colorSchemeCss 由前端包进 <style> 块）
                css += "\n@page { margin: ${margin.top}mm ${margin.right}mm ${margin.bottom}mm ${margin.left}mm; background-color: $bg; }\nhtml { background: $bg; }"
            }
        }
        return exportHtmlBlockingWithOptions(ExportController.htmlRequest(name, options, css))
    }

    private fun openSystemPdfPreview(name: String, options: ExportOptions) {
        lifecycleScope.launch {
            val exportWeb = createExportWebView(this@MainActivity)
            try {
                exportWeb.loadExportDocument(prepareExportHtml(name, options.copy(format = ExportFormat.PDF)))
                PdfExporter.openSystemPreview(this@MainActivity, exportWeb, name, options)
            } catch (e: Exception) {
                exportWeb.destroy()
                showToast("PDF 预览失败：${e.message}")
            }
        }
    }

    private fun performExport(name: String, options: ExportOptions, html: String, pdf: java.io.File?) {
        when (options.format) {
            ExportFormat.PDF -> {
                // Save the exact paginated PDF shown in the preview; never print the live editor.
                if (pdf == null) { showToast("PDF 尚未生成"); return }
                pendingPdfExport?.delete()
                pendingPdfExport = pdf
                createPdfLauncher.launch("$name.pdf")
            }
            ExportFormat.HTML -> {
                pendingHtmlExport = html
                createHtmlLauncher.launch("$name.html")
            }
            ExportFormat.IMAGE -> lifecycleScope.launch { exportAsImages(name, options, html) }
        }
    }

    /** Capture the prepared export document without resizing or replacing the editor WebView. */
    private suspend fun exportAsImages(name: String, options: ExportOptions, html: String) {
        val exportWeb = createExportWebView(this)
        try {
            val scale = options.imageScale
            val widthPx = 932 * scale
            exportWeb.setInitialScale(100 * scale)
            exportWeb.measure(
                android.view.View.MeasureSpec.makeMeasureSpec(widthPx, android.view.View.MeasureSpec.EXACTLY),
                android.view.View.MeasureSpec.makeMeasureSpec(1, android.view.View.MeasureSpec.EXACTLY),
            )
            exportWeb.layout(0, 0, widthPx, 1)
            exportWeb.loadExportDocument(html)
            val cssHeight = exportWeb.evaluateExportScript("Math.ceil(document.documentElement.scrollHeight)").toInt()
            val heightPx = (cssHeight.toLong() * scale).coerceAtLeast(1)
            require(widthPx.toLong() * heightPx <= 32_000_000) { "图片过大，请降低缩放倍数" }
            exportWeb.layout(0, 0, widthPx, heightPx.toInt())
            val bitmap = android.graphics.Bitmap.createBitmap(widthPx, heightPx.toInt(), android.graphics.Bitmap.Config.ARGB_8888)
            try {
                exportWeb.draw(android.graphics.Canvas(bitmap))
                val file = java.io.File(getExternalFilesDir(null), "${name}_${System.currentTimeMillis()}." +
                    if (options.imageFormat == "jpeg") "jpg" else "png")
                kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                    file.outputStream().use { out ->
                        bitmap.compress(if (options.imageFormat == "jpeg") android.graphics.Bitmap.CompressFormat.JPEG
                            else android.graphics.Bitmap.CompressFormat.PNG, options.imageJpegQuality, out)
                    }
                }
                showToast(L10n.f("图片已导出：%s", file.name))
            } finally { bitmap.recycle() }
        } catch (e: Exception) { showToast("导出失败：${e.message}") }
        finally { exportWeb.destroy() }
    }

    /** Request IDs prevent a cancelled preview response from completing a newer generation. */
    private suspend fun exportHtmlBlockingWithOptions(optionsText: String): String {
        val requestId = session.registerRequest("exportContent")
        val deferred = kotlinx.coroutines.CompletableDeferred<String>()
        pendingExportHtml[requestId] = deferred
        try {
            bridge.post(HostMessage.Command(
                documentId = session.documentId, revision = session.confirmedRevision, requestId = requestId,
                payload = HostMessage.Command.CommandPayload(command = "exportDocument", text = optionsText),
            ))
            return deferred.await()
        } finally {
            pendingExportHtml.remove(requestId)
            session.completeRequest(requestId, "exportContent")
        }
    }

    /** 顶栏快捷操作：按偏好顺序展开候选池；sourceToggle 双态标签
     *  （当前可视化→显示「源码」；当前源码→显示「可视化」）。 */
    private fun buildQuickActions(
        onSave: () -> Unit,
        onUndo: () -> Unit,
        onRedo: () -> Unit,
    ): List<com.markleaf.app.ui.QuickAction> {
        val undoEnabled = editorActions.value["undo"]?.first == true
        val redoEnabled = editorActions.value["redo"]?.first == true
        val hasDoc = documentLoaded.value
        return com.markleaf.app.ui.EditorPrefs.parseQuickActions(editorPrefs.value.quickActions).mapNotNull { id ->
            when (id) {
                "undo" -> com.markleaf.app.ui.QuickAction(id, L10n.t("撤销"), undoEnabled, onUndo)
                "redo" -> com.markleaf.app.ui.QuickAction(id, L10n.t("重做"), redoEnabled, onRedo)
                "save" -> com.markleaf.app.ui.QuickAction(id, L10n.t("保存"), hasDoc && !androidReadOnly.value, onSave)
                "share" -> com.markleaf.app.ui.QuickAction(id, L10n.t("分享"), hasDoc && lastKnownMarkdown.isNotEmpty()) {
                    shareActiveDocument()
                }
                "sourceToggle" -> com.markleaf.app.ui.QuickAction(
                    id,
                    if (editorSourceMode.value) L10n.t("可视化") else L10n.t("源码"),
                    hasDoc,
                ) { postCommand("toggleSourceMode") }
                "newDoc" -> com.markleaf.app.ui.QuickAction(id, L10n.t("新建"), true) { newDocument("markdown") }
                "export" -> com.markleaf.app.ui.QuickAction(id, L10n.t("导出"), hasDoc) { showExport.value = true }
                "find" -> com.markleaf.app.ui.QuickAction(id, L10n.t("查找"), hasDoc) { openFindBar(withReplace = false) }
                else -> null
            }
        }
    }

    private fun command(name: String, text: String? = null): HostMessage.Command = HostMessage.Command(
        documentId = session.documentId,
        revision = session.confirmedRevision,
        payload = HostMessage.Command.CommandPayload(command = name, text = text),
    )

    /** 不安全强调：对话框（保留字面量 / 转 HTML + 不再询问）。 */
    private fun showUnsafeEmphasisDialog(kind: String, requestId: String?) {
        val title = if (kind == "italic") L10n.t("Markdown 斜体标记可能不安全")
        else L10n.t("Markdown 粗体标记可能不安全")
        val message = if (kind == "italic")
            L10n.t("这段斜体标记可能与相邻字符连在一起。请选择保留 Markdown 字面量，或转换为 HTML 标签。")
        else L10n.t("这段粗体标记可能与相邻字符连在一起。请选择保留 Markdown 字面量，或转换为 HTML 标签。")
        val checkbox = android.widget.CheckBox(this).apply {
            text = L10n.t("不再提示并记住此选择")
            setPadding(48, 16, 48, 0)
        }
        android.app.AlertDialog.Builder(this)
            .setTitle(title)
            .setMessage(message)
            .setView(checkbox)
            .setPositiveButton(L10n.t("保留 Markdown 字面量")) { dialog, _ ->
                applyUnsafeEmphasisChoice(requestId, "literal", checkbox.isChecked)
                dialog.dismiss()
            }
            .setNegativeButton(L10n.t("转换为 HTML")) { dialog, _ ->
                applyUnsafeEmphasisChoice(requestId, "html", checkbox.isChecked)
                dialog.dismiss()
            }
            .show()
    }

    private fun applyUnsafeEmphasisChoice(requestId: String?, action: String, remember: Boolean) {
        if (remember) {
            editorPrefs.value = editorPrefs.value.copy(unsafeEmphasis = action)
            editorPrefs.value.save(prefsStore)
        }
        respondUnsafeEmphasis(requestId, action)
    }

    private fun respondUnsafeEmphasis(requestId: String?, action: String) {
        if (requestId.isNullOrBlank()) return
        bridge.post(
            HostMessage.UnsafeEmphasisResponse(
                documentId = session.documentId,
                revision = session.confirmedRevision,
                payload = kotlinx.serialization.json.buildJsonObject {
                    put("action", kotlinx.serialization.json.JsonPrimitive(action))
                },
                requestId = requestId,
            )
        )
    }

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
                // 崩溃重建：重放崩溃前内容；普通冷启动：空白未命名 Markdown 文档。
                val crashMarkdown = pendingCrashRestoreMarkdown
                pendingCrashRestoreMarkdown = null
                val activityMarkdown = pendingActivityRestoreMarkdown
                val startupDocument = StartupDocumentPolicy.initialDocument(
                    crashMarkdown ?: if (pendingActivityRestoreRef == null) activityMarkdown else null,
                )
                bridge.post(
                    HostMessage.LoadDocument(
                        documentId = frontendId,
                        revision = 0,
                        payload = HostMessage.LoadDocument.LoadDocumentPayload(
                            markdown = startupDocument.markdown,
                            documentType = startupDocument.documentType,
                        ),
                    ),
                )
                // 持久化外观（字号/行高/宽度/缩放）在首帧后应用
                android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(
                    { applyAppearance() },
                    400,
                )
                // 桌面「启动」组「启动操作」：非 newDocument 时自动重开最近
                // 工作区（Android 单窗口，openLastWorkspace 与 restoreSession
                // 行为一致）。崩溃重建/活动重建有自己的恢复路径，不叠加。
                val startupAction = editorPrefs.value.startupAction
                if ((startupAction == "openLastWorkspace" || startupAction == "restoreSession") &&
                    crashMarkdown == null && pendingActivityRestoreRef == null
                ) {
                    recentFolders().firstOrNull()?.let { (uri, _) ->
                        android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(
                            { if (workspaceRoot == null) reopenRecentFolder(uri) },
                            600,
                        )
                    }
                }
            }
            is EditorMessage.StylesApplied -> Log.i(TAG, "styles applied")
            is EditorMessage.DocumentLoaded -> {
                Log.i(TAG, "document loaded at revision ${message.revision}")
                documentLoaded.value = true
                applyEditorKernelSettings()
                // A saved instance with a SAF reference must reopen that file
                // after the initial WebView handshake. If a snapshot was saved,
                // openDocument overlays it through the existing recovery path.
                val restoredRef = pendingActivityRestoreRef
                if (restoredRef != null) {
                    pendingActivityRestoreRef = null
                    pendingActivityRestoreDocumentId?.let(recoveryStore::discard)
                    pendingActivityRestoreDocumentId = null
                    pendingRecoveryMarkdown = pendingActivityRestoreMarkdown
                    pendingRecoveryId = null
                    pendingActivityRestoreMarkdown = null
                    pendingActivityRestoreTitle = null
                    openDocument(restoredRef)
                    return
                }
                if (pendingActivityRestoreMarkdown != null) {
                    if (pendingActivityRestoreDirty) dirty.value = true
                    pendingActivityRestoreTitle?.let { activeTitle.value = it }
                    pendingActivityRestoreMarkdown = null
                    pendingActivityRestoreTitle = null
                }
                pendingActivityRestoreDocumentId?.let(recoveryStore::discard)
                pendingActivityRestoreDocumentId = null
                pendingActivityRestoreDirty = false
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
                pendingExportHtml.remove(message.requestId)?.complete(html)
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
            is EditorMessage.UnsafeEmphasisRequested -> {
                // 桌面同款流程：记住选择则直接回；否则弹对话框（可勾选不再询问）。
                // 此前 Android 未处理该消息，前端转换会一直等待响应。
                val kind = runCatching {
                    (message.payload as? kotlinx.serialization.json.JsonObject)
                        ?.get("kind")?.let { it as kotlinx.serialization.json.JsonPrimitive }
                        ?.content
                }.getOrNull() ?: "bold"
                val saved = if (editorPrefs.value.unsafeEmphasis != "ask") editorPrefs.value.unsafeEmphasis else null
                if (saved == "literal" || saved == "html") {
                    respondUnsafeEmphasis(message.requestId, saved)
                } else {
                    showUnsafeEmphasisDialog(kind, message.requestId)
                }
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
                // 原生工具条 剪切/拷贝 的取文本回包（requestId 关联）；
                // 同时缓存 text/markdown/html 三格式（复制为子菜单取用）。
                if (message.requestId != null && message.requestId == pendingSelectionExportRequestId) {
                    val obj = message.payload as? kotlinx.serialization.json.JsonObject
                    fun field(name: String): String =
                        obj?.get(name)?.let { (it as kotlinx.serialization.json.JsonPrimitive).content } ?: ""
                    val text = field("text")
                    lastSelectionExport = Triple(text, field("markdown"), field("html"))
                    pendingSelectionExport?.complete(text)
                    pendingSelectionExport = null
                    pendingSelectionExportRequestId = null
                }
            }
            is EditorMessage.BlockMenuRequested -> {
                // 块句柄长按只显示块级操作面板；正文长按仍保留原生文本选择工具条。
                selectionToolbarVisible.value = false
                selectionToolbarRect.value = null
                blockMenuVisible.value = true
            }
            is EditorMessage.FindResult -> {
                val obj = message.payload as? kotlinx.serialization.json.JsonObject
                findCurrentCount.value = (obj?.get("current") as? kotlinx.serialization.json.JsonPrimitive)
                    ?.content?.toIntOrNull() ?: 0
                findTotalCount.value = (obj?.get("total") as? kotlinx.serialization.json.JsonPrimitive)
                    ?.content?.toIntOrNull() ?: 0
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
            "\"sourceFontFamily\":'${(listOf(prefs.sourceFontFamily, prefs.sourceCjkFontFamily).filter { it.isNotBlank() } + "monospace").joinToString(", ")}'," +
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
        val normalizedPrefs = ThemeSelectionPolicy.preserveRenderedThemeWhenDisablingSystemSync(
            oldFollowSystem = old.followSystemTheme,
            newPrefs = newPrefs,
            renderedThemeId = effectiveColorThemeId(),
        )
        editorPrefs.value = normalizedPrefs
        normalizedPrefs.save(prefsStore)
        if (normalizedPrefs.zoom != old.zoom) zoomPercent.value = normalizedPrefs.zoom
        if (normalizedPrefs.styleId != old.styleId || normalizedPrefs.colorTheme != old.colorTheme ||
            normalizedPrefs.followSystemTheme != old.followSystemTheme ||
            normalizedPrefs.defaultLightTheme != old.defaultLightTheme ||
            normalizedPrefs.defaultDarkTheme != old.defaultDarkTheme
        ) {
            lastAppliedStyleId = normalizedPrefs.styleId
            applyStyles()
        }
        if (documentLoaded.value) {
            if (normalizedPrefs.codeHighlight != old.codeHighlight) {
                postCommand("setCodeHighlightVisible", if (normalizedPrefs.codeHighlight) "1" else "0")
            }
            if (normalizedPrefs.blockHandle != old.blockHandle) {
                postCommand("setBlockHandleVisible", if (normalizedPrefs.blockHandle) "1" else "0")
            }
            if (normalizedPrefs.sourceIndent != old.sourceIndent) {
                postCommand("setSourceIndent", normalizedPrefs.sourceIndent.toString())
            }
            if (normalizedPrefs.displayLanguage != old.displayLanguage) {
                L10n.language = normalizedPrefs.displayLanguage
                postCommand("setLanguage", normalizedPrefs.displayLanguage)
            }
            if (normalizedPrefs.codeFence != old.codeFence || normalizedPrefs.emphasisMarker != old.emphasisMarker ||
                normalizedPrefs.bulletMarker != old.bulletMarker ||
                normalizedPrefs.exitBlockOnEmptyEnter != old.exitBlockOnEmptyEnter ||
                normalizedPrefs.useShiftEnterHardBreak != old.useShiftEnterHardBreak ||
                normalizedPrefs.escapeLiteralSymbols != old.escapeLiteralSymbols ||
                normalizedPrefs.escapeMarkdownLiteralSymbols != old.escapeMarkdownLiteralSymbols ||
                normalizedPrefs.codeBlockSpellcheck != old.codeBlockSpellcheck
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
    /** 选区三格式导出（text/markdown/html）；失败返回 null。 */
    private suspend fun requestSelectionExport(): Triple<String, String, String>? {
        requestSelectionText()
        return lastSelectionExport?.takeIf { it.first.isNotEmpty() || it.second.isNotEmpty() || it.third.isNotEmpty() }
    }

    /** 拷贝（对齐桌面 .formatted）：纯文本为主体，HTML 附 EXTRA 供支持富文本的消费方。 */
    private fun copySelectionFormatted(text: String, html: String) {
        if (text.isEmpty() && html.isEmpty()) {
            showToast(L10n.t("当前没有可复制的文本"))
            return
        }
        val clip = if (html.isNotEmpty() && android.os.Build.VERSION.SDK_INT >= 31) {
            android.content.ClipData.newHtmlText("markleaf", text, html)
        } else {
            android.content.ClipData.newPlainText("markleaf", text)
        }
        clipboardManager().setPrimaryClip(clip)
        showToast(L10n.t("已复制"))
    }

    private fun copySelectionAs(mode: String) {
        lifecycleScope.launch {
            val export = requestSelectionExport()
            if (export == null) {
                showToast(L10n.t("当前没有可复制的文本"))
                return@launch
            }
            val (text, markdown, html) = export
            val payload = when (mode) {
                "markdown" -> markdown
                "html" -> html
                else -> text
            }
            if (payload.isEmpty()) {
                showToast(L10n.t("当前没有可复制的文本"))
                return@launch
            }
            clipboardManager().setPrimaryClip(android.content.ClipData.newPlainText("markleaf", payload))
            showToast(L10n.t("已复制"))
        }
    }

    /** 粘贴（对齐桌面 pasteFromClipboard）：剪贴板图片优先导入，其次文本交前端
     *  智能解析（markdown/HTML）。此前发送 pasteClipboard 不带内容，前端收到
     *  空串直接 failed——粘贴主路径是坏的。 */
    private fun pasteFromClipboard() {
        if (androidReadOnly.value) return
        if (!editorSourceMode.value) {
            clipboardImageUri()?.let { imageUri ->
                lifecycleScope.launch { insertImageFromSaf(imageUri) }
                return
            }
        }
        val text = clipboardManager().primaryClip?.getItemAt(0)?.coerceToText(this)
            ?.toString()?.takeIf { it.isNotEmpty() }
        if (text == null) {
            showToast(L10n.t("剪贴板中没有可粘贴的内容"))
            return
        }
        postCommand("pasteClipboard", text)
    }

    /** 粘贴为纯文本（对齐桌面 pastePlainText）：字面插入，不解析语法。 */
    private fun pastePlainTextFromClipboard() {
        if (androidReadOnly.value) return
        val text = clipboardManager().primaryClip?.getItemAt(0)?.coerceToText(this)
            ?.toString()?.takeIf { it.isNotEmpty() }
        if (text == null) {
            showToast(L10n.t("剪贴板中没有可粘贴的内容"))
            return
        }
        postCommand("pasteText", text)
    }

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

    /** 打开查找条（withReplace=true 同时展开替换行）。 */
    private fun openFindBar(withReplace: Boolean) {
        showFindBar.value = true
        showFindReplace.value = withReplace
        findCurrentCount.value = 0
        findTotalCount.value = 0
        if (findQuery.value.isNotEmpty()) sendFindText()
    }

    private fun closeFindBar() {
        showFindBar.value = false
        postCommand("findClose")
    }

    /** 查找状态串：query\tcase\twhole（对齐前端 findText 解析）。 */
    private fun findStateText(): String =
        "${findQuery.value}\t${if (findCaseSensitive.value) "1" else "0"}\t${if (findWholeWord.value) "1" else "0"}"

    private fun sendFindText() {
        if (findQuery.value.isEmpty()) return
        postCommand("findText", findStateText())
    }

    private fun findNext(backwards: Boolean) {
        if (findQuery.value.isEmpty()) return
        postCommand(if (backwards) "findPrev" else "findNext", findStateText())
    }

    private fun sendReplace(all: Boolean) {
        if (findQuery.value.isEmpty()) return
        val text = "${findQuery.value}\t${findReplaceText.value}\t" +
            "${if (findCaseSensitive.value) "1" else "0"}\t${if (findWholeWord.value) "1" else "0"}"
        postCommand(if (all) "replaceAll" else "replaceOne", text)
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
        return listOf(
            TopMenuSection(L10n.t("文件"), buildList {
                add(sub(L10n.t("新建"), children = listOf(
                    host(L10n.t("Markdown 文档")) { newDocument("markdown") },
                    host(L10n.t("纯文本")) { newDocument("plainText") },
                )))
                add(host(L10n.t("打开文件夹…"), separator = true) { openTreeLauncher.launch(null) })
                add(host(L10n.t("打开文件…")) { pendingReadOnlyOpen = false; openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) })
                add(host(L10n.t("以只读方式打开…")) { pendingReadOnlyOpen = true; openFileLauncher.launch(arrayOf("text/markdown", "text/plain")) })
                // 最近项目（对齐桌面「最近项目」子菜单：最近文件/最近文件夹
                // 两组带编号列表；空组显示 (暂无)；双记录开关均关闭时显示
                // (未启用记录)。组间以纯分隔线隔开。）
                add(sub(L10n.t("最近项目"), separator = true, children = run {
                    val files = if (editorPrefs.value.recordRecent) recentFiles() else emptyList()
                    val folders = if (editorPrefs.value.recordRecentFolders) recentFolders() else emptyList()
                    val allNames = files.map { it.second } + folders.map { it.second }
                    buildList {
                        if (editorPrefs.value.recordRecent) {
                            add(host(L10n.t("最近文件"), enabled = false) {})
                            if (files.isEmpty()) {
                                add(host(L10n.t("(暂无)"), enabled = false) {})
                            } else {
                                files.forEachIndexed { index, (uri, name) ->
                                    add(host("${index + 1}  ${recentMenuTitle(uri, name, allNames)}") {
                                        openDocument(DocumentRef.Saf(uri))
                                    })
                                }
                            }
                            add(TopMenuEntry(title = "", separatorBefore = true))
                        }
                        if (editorPrefs.value.recordRecentFolders) {
                            add(host(L10n.t("最近文件夹"), enabled = false) {})
                            if (folders.isEmpty()) {
                                add(host(L10n.t("(暂无)"), enabled = false) {})
                            } else {
                                folders.forEachIndexed { index, (uri, name) ->
                                    add(host("${index + 1}  ${recentMenuTitle(uri, name, allNames)}") {
                                        reopenRecentFolder(uri)
                                    })
                                }
                            }
                        }
                        if (!editorPrefs.value.recordRecent && !editorPrefs.value.recordRecentFolders) {
                            add(host(L10n.t("(未启用记录)"), enabled = false) {})
                        }
                    }
                }))
                add(host(L10n.t("恢复未保存的文档…"), separator = true, enabled = recoveryStore.pendingIds().isNotEmpty()) {
                    recoveryList.value = recoveryStore.pendingIds()
                        .mapNotNull { recoveryStore.consume(it) }
                        .sortedByDescending { it.savedAt }
                    if (recoveryList.value.isEmpty()) showToast(L10n.t("没有待恢复的内容"))
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
                // 分享紧跟重命名/删除组（对齐桌面 File 菜单 shareActiveTab 的位置）
                add(host(L10n.t("分享"), enabled = hasDoc && lastKnownMarkdown.isNotEmpty()) {
                    shareActiveDocument()
                })
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
            }),
            // 顺序对齐桌面 editMenu()：撤销/重做 ─ 剪切/拷贝/复制为/粘贴/粘贴为纯文本 ─ 全选 ─ 查找与替换
            TopMenuSection(L10n.t("编辑"), listOf(
                entry(L10n.t("撤销"), "undo"),
                entry(L10n.t("重做"), "redo"),
                entry(L10n.t("剪切"), "cut", separator = true),
                entry(L10n.t("拷贝"), "copy"),
                sub(L10n.t("复制为"), enabled = en("copyPlainText"), children = listOf(
                    host(L10n.t("纯文本"), enabled = en("copyPlainText")) { copySelectionAs("plain") },
                    host(L10n.t("Markdown"), enabled = en("copyMarkdown")) { copySelectionAs("markdown") },
                    host(L10n.t("HTML"), enabled = en("copyPlainText")) { copySelectionAs("html") },
                )),
                TopMenuEntry(L10n.t("粘贴"), true, false) { pasteFromClipboard() },
                host(L10n.t("粘贴为纯文本"), enabled = en("pastePlainText")) { pastePlainTextFromClipboard() },
                entry(L10n.t("全选"), "selectAll", separator = true),
                // 单入口对齐桌面：替换行在查找条内经 ⌄ 展开
                host(L10n.t("查找与替换"), enabled = documentLoaded.value, separator = true) {
                    // 对齐桌面：该入口始终带替换行展开
                    openFindBar(withReplace = true)
                },
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
                // 单项目子菜单拍平：点击直接弹行列对话框
                entry(L10n.t("表格…"), "insertTable", action = { showTableSizeDialog.value = true }),
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
                // 上下文相关区块：仅在语义上下文匹配时出现（光标在表格内 /
                // 图片选中），替代永久置灰——与底部选择工具条同源
                // （CommandStateChanged.semanticContext）。
                if (editorSemanticContext.value == "table") add(sub(L10n.t("表格"), enabled = en("addRowAfter"), separator = true, children = listOf(
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
                if (editorSemanticContext.value == "image") add(sub(L10n.t("图片"), enabled = en("rotateImageClockwise"), separator = true, children = listOf(
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
                // 侧栏开关不设：侧栏是抽屉（☰ 打开/遮罩关闭），菜单项冗余；
                // 专注/打字机不接入：手机视口小，淡化邻段收益低，且打字机的
                // 强制滚动与触摸滚动、IME inset 处理相冲突（桌面滚轮习惯的
                // 反模式）。
                host(L10n.t("源码模式"), enabled = hasDoc, checked = editorSourceMode.value) { postCommand("toggleSourceMode") },
                host(L10n.t("只读模式"), enabled = hasDoc, checked = androidReadOnly.value, separator = true) {
                    toggleReadOnly()
                },
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
        // 重置会话基线：当前文档（如欢迎页输入）已把 confirmedRevision 推进到
        // N，而前端 loadDocument 会把自身 revision 归 0——不重置的话新文档的
        // 所有消息（rev<N）都被会话守卫当作过期消息拒收，编辑器表现为冻结。
        session.startDocument(session.documentId, revision = 0)
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
            // 重置会话基线（同 newDocument：欢迎文档消息在新基线之下才会被接受）
            session.startDocument(session.documentId, revision = 0)
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
        workspaceOpen.value = false
        sidebarEntries.value = emptyList()
        showToast(L10n.t("已关闭工作区"))
    }

    /** 最近项目菜单标题：重名时以「名称 — 父目录」消歧（对齐桌面
     *  recentMenuTitle）。父目录取 SAF URI document/tree 段路径的倒数
     *  第二段；tree 根目录无父段时保持原名称。 */
    private fun recentMenuTitle(uri: String, name: String, allNames: List<String>): String {
        val duplicate = allNames.count { it.equals(name, ignoreCase = true) } > 1
        if (!duplicate) return name
        val decoded = android.net.Uri.decode(uri)
            .substringAfterLast("/document/", "").substringAfterLast("/tree/", "")
        val segments = decoded.substringAfter(':').split('/').filter { it.isNotEmpty() }
        val parent = segments.getOrNull(segments.size - 2) ?: return name
        return "$name — $parent"
    }

    /** 最近文件：SharedPreferences 持久化（uri|名称 按行，最多 10 条，
     *  新开顶置）。记录开关关闭时不新增（对齐桌面 recordRecentFiles：
     *  菜单里该组整体隐藏）。 */
    private fun rememberRecentFile(uri: String, name: String) {
        if (!editorPrefs.value.recordRecent) return
        val current = recentFiles().filter { it.first != uri }.toMutableList()
        current.add(0, uri to name)
        val trimmed = current.take(10)
        prefsStore.edit().putString("recentFiles", trimmed.joinToString("\n") { "${it.first}|${it.second}" }).apply()
    }

    /** 记录最近文件夹（对齐桌面 recordRecentFolders；tree URI 已持持久化权限）。 */
    private fun rememberRecentFolder(uri: String, name: String) {
        if (!editorPrefs.value.recordRecentFolders) return
        val current = recentFolders().filter { it.first != uri }.toMutableList()
        current.add(0, uri to name)
        val trimmed = current.take(10)
        prefsStore.edit().putString("recentFolders", trimmed.joinToString("\n") { "${it.first}|${it.second}" }).apply()
    }

    private fun clearRecentHistory() {
        // 确认对话框在 PreferencesScreen（Compose 同主题）里弹出后调用；
        // 这里只执行清除。对齐桌面：不可恢复的数据删除前必须确认。
        prefsStore.edit().remove("recentFiles").remove("recentFolders").apply()
        showToast(L10n.t("历史记录已清除"))
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
            // 「引用原图」：SAF 文档 id 以文档目录为前缀 → 引用树内相对路径，
            // 不复制（资源解析按文档目录行走，树外图片无法解析）。
            if (editorPrefs.value.fileImageHandling == "referenceOriginal") {
                originalReferenceWithinDocTree(docDirUri, sourceUri)?.let { return@runCatching it }
                showToast(L10n.t("原图不在文档目录内，已复制到 assets"))
            }
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

    /** 引用原图：SAF 文档 id 以文档目录 documentId 为前缀 → 目录内相对引用；
     *  树外图片无法被编辑器资源解析（AssetResolver 按文档目录行走），返回 null 走复制。 */
    private fun originalReferenceWithinDocTree(docDirUri: String, imageUri: Uri): String? {
        val imageId = runCatching { android.provider.DocumentsContract.getDocumentId(imageUri) }.getOrNull()
            ?: return null
        val docBaseId = runCatching {
            val uri = Uri.parse(docDirUri)
            if (uri.pathSegments.contains("tree")) {
                android.provider.DocumentsContract.getTreeDocumentId(uri)
            } else {
                android.provider.DocumentsContract.getDocumentId(uri)
            }
        }.getOrNull() ?: return null
        if (docBaseId.isEmpty() || imageId == docBaseId || !imageId.startsWith("$docBaseId/")) return null
        val relative = imageId.removePrefix("$docBaseId/").replace(" ", "%20")
        if (relative.isBlank()) return null
        return if (editorPrefs.value.prefixRelativeWithDotSlash) "./$relative" else relative
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
        workspaceOpen.value = true
        refreshWorkspace()
    }

    /** 侧栏 ＋ / 长按菜单新建文件：目标文件夹优先用调用方给定（长按文件
     *  夹时），否则当前文档所在文件夹/工作区根（对齐桌面
     *  newMarkdownFileFromHeader）；建议名避让后弹名称对话框。 */
    private fun promptNewWorkspaceFile(folder: DocumentRef.Saf? = null, kind: String = "markdown") {
        val root = workspaceRoot ?: return
        val target = folder ?: currentDocFolderInWorkspace(root) ?: rootFolderRef(root)
        newFileKind = kind
        lifecycleScope.launch {
            val taken = runCatching { source.listChildren(target).map { it.name } }
                .getOrDefault(emptyList())
            newFileFolderRef = target
            val base = L10n.t(if (kind == "plainText") "未命名.txt" else "未命名.md")
            val suggested = availableWorkspaceName(base, taken)
            newFileNameInput.value = androidx.compose.ui.text.input.TextFieldValue(
                text = suggested,
                selection = androidx.compose.ui.text.TextRange(0, suggested.length),
            )
            showNewFileDialog.value = true
        }
    }

    /** 侧栏长按菜单新建文件夹（对齐桌面 createWorkspaceFolder：预填避让名）。 */
    private fun promptNewWorkspaceFolder(folder: DocumentRef.Saf? = null) {
        val root = workspaceRoot ?: return
        val target = folder ?: rootFolderRef(root)
        lifecycleScope.launch {
            val taken = runCatching { source.listChildren(target).map { it.name } }
                .getOrDefault(emptyList())
            newFolderRef = target
            val suggested = availableWorkspaceName(L10n.t("新建文件夹"), taken)
            newFolderNameInput.value = androidx.compose.ui.text.input.TextFieldValue(
                text = suggested,
                selection = androidx.compose.ui.text.TextRange(0, suggested.length),
            )
            showNewFolderDialog.value = true
        }
    }

    /** 从对话框创建工作区文件夹：重名拦截，创建后刷新侧栏。 */
    private fun createWorkspaceFolderFromDialog() {
        val folder = newFolderRef ?: return
        val name = newFolderNameInput.value.text.trim()
        if (name.isEmpty()) return
        showNewFolderDialog.value = false
        lifecycleScope.launch {
            val duplicate = runCatching { source.listChildren(folder).map { it.name } }
                .getOrDefault(emptyList())
                .any { it.equals(name, ignoreCase = true) }
            if (duplicate) {
                showToast(L10n.t("同名文件或文件夹已存在。"))
                return@launch
            }
            runCatching { source.createFolder(folder, name) }
                .onSuccess {
                    refreshWorkspace()
                    showToast(L10n.f("已创建文件夹 %s", name))
                }
                .onFailure { showToast(L10n.f("创建文件夹失败：%s", it.message)) }
        }
    }

    /** 复制条目路径（SAF URI）到剪贴板。 */
    private fun copyEntryPath(entry: com.markleaf.app.documents.DocumentEntry) {
        val uri = (entry.ref as? DocumentRef.Saf)?.uri ?: return
        clipboardManager().setPrimaryClip(
            android.content.ClipData.newPlainText("path", uri),
        )
        showToast(L10n.t("已复制文件 URI"))
    }

    /** 复制文件内容（按探测编码解码，去 BOM）到剪贴板。 */
    private fun copyEntryContent(entry: com.markleaf.app.documents.DocumentEntry) {
        val safRef = entry.ref as? DocumentRef.Saf ?: return
        lifecycleScope.launch {
            runCatching { source.read(safRef) }
                .onSuccess { bytes ->
                    val charset = runCatching {
                        java.nio.charset.Charset.forName(bytes.encoding.name)
                    }.getOrDefault(Charsets.UTF_8)
                    var text = String(bytes.bytes, charset)
                    if (bytes.encoding.hasBom) text = text.removePrefix("\uFEFF")
                    clipboardManager().setPrimaryClip(
                        android.content.ClipData.newPlainText("markdown", text),
                    )
                    showToast(L10n.t("已复制文件内容"))
                }
                .onFailure { showToast(L10n.f("读取失败：%s", it.message)) }
        }
    }

    /** 分享（对齐桌面 shareEntry）：以内容 URI 走系统分享面板。 */
    private fun shareEntry(entry: com.markleaf.app.documents.DocumentEntry) {
        val safUri = (entry.ref as? DocumentRef.Saf)?.uri ?: return
        val uri = Uri.parse(safUri)
        val intent = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
            type = "text/markdown"
            putExtra(android.content.Intent.EXTRA_STREAM, uri)
            clipData = android.content.ClipData.newRawUri(entry.name, uri)
            addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        startActivity(android.content.Intent.createChooser(intent, entry.name))
    }

    /** 「复制文件内容」内容探测（对齐桌面 hasCopyableContent 的非空判定；
     *  扩展名过滤在调用侧先行）。 */
    private suspend fun entryHasCopyableContent(entry: com.markleaf.app.documents.DocumentEntry): Boolean =
        (entry.ref as? DocumentRef.Saf)?.let { ref ->
            runCatching { source.read(ref).bytes.isNotEmpty() }.getOrDefault(false)
        } ?: false

    /** 当前文档所在文件夹（在工作区内时返回其 SAF 引用）；否则 null。
     *  通过 tree document id 前缀判断归属，父 id 取最后一段前缀。 */
    private fun currentDocFolderInWorkspace(root: DocumentRef.Saf): DocumentRef.Saf? {
        val docRef = controller?.activeRef as? DocumentRef.Saf ?: return null
        val treeUri = root.parsedUri
        val treeId = android.provider.DocumentsContract.getTreeDocumentId(treeUri)
        val docId = android.provider.DocumentsContract.getDocumentId(docRef.parsedUri)
        if (docId != treeId && !docId.startsWith("$treeId/")) return null
        val parentDocId = docId.substringBeforeLast('/', missingDelimiterValue = treeId)
        val folderUri = android.provider.DocumentsContract.buildDocumentUriUsingTree(treeUri, parentDocId)
        return DocumentRef.Saf(folderUri.toString())
    }

    /** tree 引用 → 工作区根目录的 document 引用：createDocument 需要父级
     *  为 document uri，直接传 tree uri 会被 SAF 拒绝。 */
    private fun rootFolderRef(root: DocumentRef.Saf): DocumentRef.Saf {
        val treeUri = root.parsedUri
        val docUri = android.provider.DocumentsContract.buildDocumentUriUsingTree(
            treeUri,
            android.provider.DocumentsContract.getTreeDocumentId(treeUri),
        )
        return DocumentRef.Saf(docUri.toString())
    }

    /** 对齐桌面 availableWorkspaceName：base 未占用直接用；否则追加
     *  " 2"、" 3"…（保留扩展名），大小写不敏感比较。 */
    private fun availableWorkspaceName(base: String, taken: List<String>): String {
        if (taken.none { it.equals(base, ignoreCase = true) }) return base
        val dot = base.lastIndexOf('.')
        val stem = if (dot > 0) base.substring(0, dot) else base
        val ext = if (dot > 0) base.substring(dot) else ""
        var index = 2
        while (true) {
            val candidate = "$stem $index$ext"
            if (taken.none { it.equals(candidate, ignoreCase = true) }) return candidate
            index++
        }
    }

    /** 对齐桌面 createWorkspaceFile：扩展名归一（无 md/markdown/txt 后缀
     *  → 追加 .md）、重名拦截、创建后打开新文件并重扫侧栏。 */
    private fun createWorkspaceFileFromDialog() {
        val folder = newFileFolderRef ?: return
        var name = newFileNameInput.value.text.trim()
        if (name.isEmpty()) return
        val ext = name.substringAfterLast('.', "").lowercase()
        if (ext != "md" && ext != "markdown" && ext != "txt") name += ".md"
        showNewFileDialog.value = false
        lifecycleScope.launch {
            val duplicate = runCatching { source.listChildren(folder).map { it.name } }
                .getOrDefault(emptyList())
                .any { it.equals(name, ignoreCase = true) }
            if (duplicate) {
                showToast(L10n.t("同名文件或文件夹已存在。"))
                return@launch
            }
            runCatching { source.createDocument(folder, name) as DocumentRef.Saf }
                .onSuccess { ref ->
                    refreshWorkspace()
                    openDocument(ref)
                    showToast(L10n.f("已创建文件 %s", name))
                }
                .onFailure {
                    showToast(L10n.f("创建文件失败：%s", it.message))
                }
        }
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
        // 列表弹窗打开时移除该条（其余快照仍可选择）
        recoveryList.value = recoveryList.value.filter { it.documentId != entry.documentId }
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

    override fun onSaveInstanceState(outState: Bundle) {
        // lastKnownMarkdown is refreshed by the normal snapshot/recovery loop.
        // Saving it here closes the rotation/process-recreation gap without
        // synchronously blocking the UI thread while WebView evaluates JS.
        outState.putString(STATE_MARKDOWN, lastKnownMarkdown.takeIf { dirty.value && it.isNotEmpty() })
        outState.putString(STATE_REF_URI, controller?.activeRef?.uri)
        outState.putString(STATE_DOCUMENT_ID, session.documentId)
        outState.putString(STATE_TITLE, activeTitle.value)
        outState.putBoolean(STATE_DIRTY, dirty.value)
        super.onSaveInstanceState(outState)
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.action == KeyEvent.ACTION_DOWN && event.keyCode == KeyEvent.KEYCODE_ESCAPE) {
            if (dismissKeyboardOverlay()) return true
        }
        val command = KeyboardShortcutPolicy.commandFor(event)
        if (command != null && editorHasFocus()) {
            when (command) {
                "save" -> if (documentLoaded.value) lifecycleScope.launch { saveActiveDocument() }
                "find" -> if (documentLoaded.value) openFindBar(withReplace = false)
                else -> if (documentLoaded.value) postCommand(command)
            }
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    private fun editorHasFocus(): Boolean =
        ::webView.isInitialized && (webView.hasFocus() || currentFocus === webView)

    private fun dismissKeyboardOverlay(): Boolean {
        return when {
            showFindBar.value -> {
                closeFindBar()
                true
            }
            showExport.value -> {
                showExport.value = false
                true
            }
            showPreferences.value -> {
                showPreferences.value = false
                true
            }
            showAbout.value -> {
                showAbout.value = false
                true
            }
            blockMenuVisible.value -> {
                dismissBlockMenu()
                true
            }
            selectionToolbarVisible.value -> {
                toolbarHandler.removeCallbacks(showSelectionToolbarRunnable)
                selectionToolbarVisible.value = false
                selectionToolbarRect.value = null
                true
            }
            else -> false
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

    private data class BlockMenuItem(
        val title: String,
        val command: String,
        val separatorBefore: Boolean = false,
        val languagePicker: Boolean = false,
        val text: String? = null,
    )

    private data class BlockMenuGroup(
        val title: String,
        val items: List<BlockMenuItem>,
    )

    private fun dismissBlockMenu() {
        if (!blockMenuVisible.value) return
        blockMenuVisible.value = false
        postCommand("clearBlockHighlight")
    }

    private fun blockMenuGroups(
        semanticContext: String?,
        blockType: String?,
        actions: Map<String, Pair<Boolean, Boolean>>,
    ): List<BlockMenuGroup> {
        fun item(title: String, command: String, languagePicker: Boolean = false, text: String? = null) =
            BlockMenuItem(title, command, languagePicker = languagePicker, text = text)
        fun group(title: String, vararg items: BlockMenuItem): BlockMenuGroup =
            BlockMenuGroup(title, items.filter { actions[it.command]?.first == true })

        val groups = mutableListOf(
            group(
                L10n.t("编辑"),
                item(L10n.t("撤销"), "undo"),
                item(L10n.t("重做"), "redo"),
                item(L10n.t("上方插入段落"), "insertLineBefore"),
                item(L10n.t("下方插入段落"), "insertLineAfter"),
                item(L10n.t("复制段落"), "duplicateParagraph"),
                item(L10n.t("删除段落"), "deleteParagraph"),
            ),
            group(
                if (blockType?.startsWith("heading") == true) L10n.t("标题")
                else if (blockType?.endsWith("List") == true) L10n.t("列表")
                else L10n.t("段落"),
                item(L10n.t("正文"), "setParagraph"),
                item(L10n.t("一级标题"), "setHeading1"),
                item(L10n.t("二级标题"), "setHeading2"),
                item(L10n.t("三级标题"), "setHeading3"),
                item(L10n.t("四级标题"), "setHeading4"),
                item(L10n.t("五级标题"), "setHeading5"),
                item(L10n.t("六级标题"), "setHeading6"),
                item(L10n.t("提升标题级别"), "promoteHeading"),
                item(L10n.t("降低标题级别"), "demoteHeading"),
                item(L10n.t("引用"), "toggleBlockquote"),
                item(L10n.t("无序列表"), "toggleBulletList"),
                item(L10n.t("有序列表"), "toggleOrderedList"),
                item(L10n.t("任务列表"), "toggleTaskList"),
                item(L10n.t("增加列表缩进"), "indentListItem"),
                item(L10n.t("减少列表缩进"), "outdentListItem"),
                item(L10n.t("代码块"), "toggleCodeBlock"),
            ),
            group(
                L10n.t("插入"),
                item(L10n.t("行内公式"), "insertMathInline"),
                item(L10n.t("块级公式"), "insertMathBlock"),
                item(L10n.t("表格"), "insertTable"),
                item(L10n.t("水平分隔线"), "insertHorizontalRule"),
                item(L10n.t("Mermaid 图表"), "insertMermaid"),
            ),
            group(
                L10n.t("格式"),
                item(L10n.t("粗体"), "toggleBold"),
                item(L10n.t("斜体"), "toggleItalic"),
                item(L10n.t("下划线"), "toggleUnderline"),
                item(L10n.t("删除线"), "toggleStrike"),
                item(L10n.t("高亮"), "toggleHighlight"),
                item(L10n.t("行内代码"), "toggleCode"),
                item(L10n.t("格式刷"), "formatPainter"),
                item(L10n.t("清除格式"), "clearFormat"),
            ),
        )

        val contextItems = when (semanticContext) {
            "codeBlock" -> listOf(
                item(L10n.t("声明代码语言…"), "setCodeBlockLanguage", languagePicker = true),
                item(L10n.t("格式化代码块"), "formatCodeBlock"),
                item(L10n.t("复制整段代码"), "copyCodeBlock"),
                item(L10n.t("转为正文"), "exitCode"),
            )
            "table" -> listOf(
                item(L10n.t("上方插行"), "addRowBefore"),
                item(L10n.t("下方插行"), "addRowAfter"),
                item(L10n.t("删除当前行"), "deleteRow"),
                item(L10n.t("左插列"), "addColumnBefore"),
                item(L10n.t("右插列"), "addColumnAfter"),
                item(L10n.t("删除当前列"), "deleteColumn"),
                item(L10n.t("根据内容调整列宽"), "fitTableContents"),
                item(L10n.t("平均分配列宽"), "distributeTableColumns"),
                item(L10n.t("恢复自动列宽"), "resetTableWidths"),
                item(L10n.t("左对齐"), "alignTableLeft"),
                item(L10n.t("居中对齐"), "alignTableCenter"),
                item(L10n.t("右对齐"), "alignTableRight"),
                item(L10n.t("删除表格"), "deleteTable"),
            )
            "math" -> listOf(
                item(L10n.t("编辑公式源码"), "editMath"),
                item(L10n.t("公式编号…"), "setMathNumber"),
                item(L10n.t("转为行内/块级"), "convertMath"),
                item(L10n.t("删除公式"), "deleteMath"),
            )
            "mermaid" -> listOf(
                item(L10n.t("编辑 Mermaid 源码"), "editMermaid"),
                item(L10n.t("重新渲染图表"), "rerenderMermaid"),
                item(L10n.t("保存图表"), "saveMermaidAs"),
                item(L10n.t("删除图表"), "deleteMermaid"),
            )
            "image" -> listOf(
                item(L10n.t("50%"), "resizeImage", text = "50"),
                item(L10n.t("75%"), "resizeImage", text = "75"),
                item(L10n.t("90%"), "resizeImage", text = "90"),
                item(L10n.t("100%"), "resizeImage", text = "100"),
                item(L10n.t("顺时针旋转"), "rotateImageClockwise"),
                item(L10n.t("保存图片"), "saveImageAs"),
            )
            else -> emptyList()
        }
        if (contextItems.isNotEmpty()) groups += BlockMenuGroup(
            when (semanticContext) {
                "codeBlock" -> L10n.t("代码块")
                "table" -> L10n.t("表格")
                "math" -> L10n.t("公式")
                "mermaid" -> L10n.t("Mermaid")
                "image" -> L10n.t("图片")
                else -> L10n.t("上下文")
            },
            contextItems.filter { actions[it.command]?.first == true },
        )
        return groups.filter { it.items.isNotEmpty() }
    }

    @OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
    @Composable
    private fun BlockMenuSheet(
        semanticContext: String?,
        actions: Map<String, Pair<Boolean, Boolean>>,
        onDismiss: () -> Unit,
        onCommand: (String, String?) -> Unit,
        onLanguagePicker: () -> Unit,
    ) {
        val blockType = (editorStatus.value?.get("blockType") as? kotlinx.serialization.json.JsonPrimitive)?.content
        val groups = blockMenuGroups(semanticContext, blockType, actions)
        var selectedGroup by remember(groups) { mutableStateOf(0) }
        androidx.compose.material3.ModalBottomSheet(onDismissRequest = onDismiss) {
            Text(
                L10n.f("%s操作", blockTypeLabel(blockType)),
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp),
            )
            androidx.compose.material3.ScrollableTabRow(
                selectedTabIndex = selectedGroup.coerceIn(0, (groups.size - 1).coerceAtLeast(0)),
                edgePadding = 16.dp,
            ) {
                groups.forEachIndexed { index, group ->
                    androidx.compose.material3.Tab(
                        selected = selectedGroup == index,
                        onClick = { selectedGroup = index },
                        text = { Text(group.title) },
                    )
                }
            }
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(max = 520.dp)
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            ) {
                val group = groups.getOrNull(selectedGroup.coerceIn(0, (groups.size - 1).coerceAtLeast(0)))
                androidx.compose.foundation.layout.FlowRow(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    group?.items?.forEach { item ->
                        val enabled = actions[item.command]?.first == true
                        androidx.compose.material3.FilterChip(
                            selected = false,
                            enabled = enabled,
                            onClick = { if (item.languagePicker) onLanguagePicker() else onCommand(item.command, item.text) },
                            label = { Text(item.title) },
                        )
                    }
                }
            }
        }
    }

    private fun blockTypeLabel(blockType: String?): String = when {
        blockType == null -> L10n.t("段落")
        blockType.startsWith("heading") -> L10n.t("标题")
        blockType.endsWith("List") -> L10n.t("列表")
        else -> when (blockType) {
            "codeBlock" -> L10n.t("代码块")
            "table" -> L10n.t("表格")
            "image" -> L10n.t("图片")
            else -> L10n.t("段落")
        }
    }

    /** 查找与替换条：驱动内核查找命令（findText/findNext/replaceOne/replaceAll/
     *  findClose），结果由前端 findResult 回报（current/total）。无前端改动。 */
    @Composable
    private fun FindReplaceBar() {
        val focusRequester = findFocusRequester
        androidx.compose.foundation.layout.Column(
            modifier = Modifier
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surfaceVariant)
                .padding(horizontal = 8.dp, vertical = 4.dp),
        ) {
            // 行1：查找框 + 计数 + 上一个/下一个 + 关闭
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                androidx.compose.material3.OutlinedTextField(
                    value = findQuery.value,
                    onValueChange = {
                        findQuery.value = it
                        sendFindText()
                    },
                    modifier = Modifier.weight(1f).focusRequester(focusRequester)
                        .heightIn(min = 48.dp),
                    singleLine = true,
                    textStyle = MaterialTheme.typography.bodyMedium,
                    placeholder = { Text(L10n.t("查找"), style = MaterialTheme.typography.bodySmall) },
                    keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
                        imeAction = androidx.compose.ui.text.input.ImeAction.Search,
                    ),
                    keyboardActions = androidx.compose.foundation.text.KeyboardActions(
                        onSearch = { findNext(backwards = false) },
                    ),
                )
                Text(
                    text = "${findCurrentCount.value}/${findTotalCount.value}",
                    modifier = Modifier.padding(horizontal = 6.dp),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                TextButton(onClick = { findNext(backwards = true) }, enabled = findQuery.value.isNotEmpty()) {
                    Text("↑")
                }
                TextButton(onClick = { findNext(backwards = false) }, enabled = findQuery.value.isNotEmpty()) {
                    Text("↓")
                }
                TextButton(onClick = { showFindReplace.value = !showFindReplace.value }) {
                    Text(if (showFindReplace.value) "⌃" else "⌄")
                }
                TextButton(onClick = { closeFindBar() }) { Text("✕") }
            }
            // 行2：选项 + 替换展开开关
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                androidx.compose.material3.FilterChip(
                    selected = findCaseSensitive.value,
                    onClick = {
                        findCaseSensitive.value = !findCaseSensitive.value
                        sendFindText()
                    },
                    label = { Text("Aa", style = MaterialTheme.typography.labelMedium) },
                )
                Text(
                    L10n.t("区分大小写"),
                    modifier = Modifier.padding(start = 2.dp, end = 10.dp),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                androidx.compose.material3.FilterChip(
                    selected = findWholeWord.value,
                    onClick = {
                        findWholeWord.value = !findWholeWord.value
                        sendFindText()
                    },
                    label = { Text(L10n.t("全字匹配"), style = MaterialTheme.typography.labelMedium) },
                )
            }
            // 行3（可展开）：替换行（展开/收起 110ms，与菜单面板一致）
            androidx.compose.animation.AnimatedVisibility(
                visible = showFindReplace.value,
                enter = androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(110)) +
                    androidx.compose.animation.expandVertically(androidx.compose.animation.core.tween(110)),
                exit = androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(110)) +
                    androidx.compose.animation.shrinkVertically(androidx.compose.animation.core.tween(110)),
            ) {
                Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    androidx.compose.material3.OutlinedTextField(
                        value = findReplaceText.value,
                        onValueChange = { findReplaceText.value = it },
                        modifier = Modifier.weight(1f).heightIn(min = 48.dp),
                        singleLine = true,
                        textStyle = MaterialTheme.typography.bodyMedium,
                        placeholder = { Text(L10n.t("替换"), style = MaterialTheme.typography.bodySmall) },
                        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
                            imeAction = androidx.compose.ui.text.input.ImeAction.Done,
                        ),
                        keyboardActions = androidx.compose.foundation.text.KeyboardActions(
                            onDone = { sendReplace(all = false) },
                        ),
                    )
                    TextButton(
                        onClick = { sendReplace(all = false) },
                        enabled = findQuery.value.isNotEmpty(),
                    ) { Text(L10n.t("替换")) }
                    TextButton(
                        onClick = { sendReplace(all = true) },
                        enabled = findQuery.value.isNotEmpty(),
                    ) { Text(L10n.t("全部替换")) }
                }
            }
            // 打开时聚焦查找框（拉起键盘）
            androidx.compose.runtime.LaunchedEffect(showFindBar.value) {
                if (showFindBar.value) focusRequester.requestFocus()
            }
        }
    }

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
            SelectionToolbarItem(L10n.t("粘贴"), actions["paste"]?.first == true, action = { pasteFromClipboard() }),
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
        private const val STATE_MARKDOWN = "markleaf.state.markdown"
        private const val STATE_REF_URI = "markleaf.state.refUri"
        private const val STATE_DOCUMENT_ID = "markleaf.state.documentId"
        private const val STATE_TITLE = "markleaf.state.title"
        private const val STATE_DIRTY = "markleaf.state.dirty"
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
                    .statusBarAction(onEncoding),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarNewline) {
            Text(
                text = newline,
                modifier = Modifier
                    .statusBarAction(onNewline),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarModeToggle) {
            Text(
                text = if (sourceMode) L10n.t("源码") else L10n.t("可视化"),
                modifier = Modifier
                    .statusBarAction(onToggleMode),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarZoom) {
            Text(
                text = "$zoom%",
                modifier = Modifier
                    .statusBarAction(onZoom),
                style = itemStyle, color = itemColor,
            )
        }
        if (prefs.statusBarWordCount && characterCount.isNotEmpty()) {
            // 对齐桌面：字数按钮点击弹出文档统计面板
            Text(
                text = "$characterCount ${L10n.t("字符")}",
                modifier = Modifier
                    .statusBarAction(onShowStatistics),
                style = itemStyle,
                color = itemColor,
            )
        }
    }
}

private fun Modifier.statusBarAction(onClick: () -> Unit): Modifier =
    clip(RoundedCornerShape(8.dp))
        .clickable(onClick = onClick)
        .padding(horizontal = 6.dp, vertical = 2.dp)

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

/** 侧栏长按菜单行（bottom sheet 内），对齐桌面右键菜单的条目形态；
 *  destructive 项（删除）用 error 色。 */
@androidx.compose.runtime.Composable
private fun SheetMenuRow(
    title: String,
    enabled: Boolean = true,
    destructive: Boolean = false,
    onClick: () -> Unit,
) {
    androidx.compose.foundation.layout.Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 24.dp, vertical = 14.dp),
        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
    ) {
        androidx.compose.material3.Text(
            title,
            style = androidx.compose.material3.MaterialTheme.typography.bodyLarge,
            color = when {
                !enabled -> androidx.compose.material3.MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f)
                destructive -> androidx.compose.material3.MaterialTheme.colorScheme.error
                else -> androidx.compose.material3.MaterialTheme.colorScheme.onSurface
            },
        )
    }
}

/** 侧栏长按菜单分组分隔线。 */
@androidx.compose.runtime.Composable
private fun SheetDivider() {
    androidx.compose.material3.HorizontalDivider(
        modifier = Modifier.padding(horizontal = 24.dp, vertical = 4.dp),
        color = androidx.compose.material3.MaterialTheme.colorScheme.outlineVariant,
    )
}
