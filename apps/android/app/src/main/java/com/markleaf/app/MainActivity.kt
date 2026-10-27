package com.markleaf.app

import android.os.Bundle
import android.util.Log
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
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
import androidx.compose.ui.viewinterop.AndroidView
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
    private val editorStatusLine = mutableStateOf("")
    private val sidebarEntries = mutableStateOf<List<DocumentEntry>>(emptyList())
    private val outline = mutableStateOf<List<OutlineItem>>(emptyList())
    private val activeTitle = mutableStateOf("未命名")
    private val showExport = mutableStateOf(false)
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
com.markleaf.app.documents.SafDocumentSource.takePersistablePermission(this, uri)
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
                editorStatusLine.value = "HTML 已导出"
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

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
                        Column(modifier = Modifier.fillMaxSize()) {
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
                            EditorContainer(webView)
                            StatusBarPlaceholder(editorStatusLine.value)
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
                    editorStatusLine.value = "文档权限失效，请重新打开"
                }
                is DocumentController.OpenResult.Failed -> {
                    editorStatusLine.value = "打开失败: ${result.message}"
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
                editorStatusLine.value = "已保存"
            }
            DocumentController.SaveResult.Conflict -> editorStatusLine.value = "文件已被外部修改，请另存或覆盖"
            DocumentController.SaveResult.PermissionLost -> editorStatusLine.value = "保存失败：权限失效"
            DocumentController.SaveResult.NotDirty -> Unit
            is DocumentController.SaveResult.Failed -> editorStatusLine.value = "保存失败: ${result.message}"
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
            is EditorMessage.DirtyChanged -> dirty.value = message.payload.dirty
            is EditorMessage.OutlineChanged -> outline.value = parseOutline(message.payload)
            is EditorMessage.EditorStatusChanged -> editorStatusLine.value = formatStatus(message.payload)
            else -> Unit
        }
    }

    /** editorStatusChanged payload → 状态栏文本（不裸 dump JSON）。 */
    private fun formatStatus(payload: kotlinx.serialization.json.JsonElement?): String {
        val obj = payload as? kotlinx.serialization.json.JsonObject ?: return ""
        fun field(name: String): String? =
            (obj[name] as? kotlinx.serialization.json.JsonPrimitive)?.content
        val parts = mutableListOf<String>()
        (obj["characterCount"] as? kotlinx.serialization.json.JsonPrimitive)?.content
            ?.let { parts.add("字数 $it") }
        val line = field("line")
        val column = field("column")
        if (line != null && column != null) {
            parts.add("第 $line 行, 第 $column 列")
        }
        return parts.joinToString(" · ")
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
private fun androidx.compose.foundation.layout.ColumnScope.EditorContainer(webView: WebView) {
    AndroidView(
        factory = { webView },
        modifier = Modifier
            .fillMaxWidth()
            .weight(1f),
    )
}

@Composable
private fun StatusBarPlaceholder(statusLine: String) {
    Text(
        text = statusLine.ifEmpty { "MarkLeaf" },
        modifier = Modifier
            .fillMaxWidth()
            .height(28.dp)
            .padding(horizontal = 12.dp, vertical = 4.dp),
        style = MaterialTheme.typography.labelSmall,
    )
}

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
