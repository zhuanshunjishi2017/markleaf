package com.markleaf.app

import android.os.Bundle
import android.util.Log
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.markleaf.app.bridge.EditorBridge
import com.markleaf.app.bridge.EditorMessage
import com.markleaf.app.bridge.EditorSessionState
import com.markleaf.app.bridge.HostMessage
import com.markleaf.app.editor.EditorWebViewFactory

class MainActivity : ComponentActivity() {

    private lateinit var bridge: EditorBridge
    private lateinit var session: EditorSessionState
    private val documentLoaded = mutableStateOf(false)
    private val editorStatusLine = mutableStateOf("")

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val webView = EditorWebViewFactory.create(this)
        session = EditorSessionState().apply { startDocument(WELCOME_DOCUMENT_ID) }
        bridge = EditorBridge(
            postScript = { script -> webView.evaluateJavascript(script, null) },
            session = session,
        )
        bridge.attach(webView)

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    Column(modifier = Modifier.fillMaxSize()) {
                        TabStripPlaceholder(active = "未命名", documentLoaded = documentLoaded.value)
                        EditorContainer(webView)
                        StatusBarPlaceholder(editorStatusLine.value)
                    }
                }
            }
        }

        bridge.onEditorMessageDispatcher = ::handleEditorMessage
        webView.loadUrl(EDITOR_URL)
    }

    private fun handleEditorMessage(message: EditorMessage) {
        when (message) {
            is EditorMessage.Ready -> {
                Log.i(TAG, "editor ready; sending styles + welcome document")
                bridge.post(HostMessage.ApplyStyles(documentId = WELCOME_DOCUMENT_ID, revision = 0))
                bridge.post(welcomeDocumentMessage())
            }
            is EditorMessage.StylesApplied -> Log.i(TAG, "styles applied")
            is EditorMessage.DocumentLoaded -> {
                Log.i(TAG, "document loaded at revision ${message.revision}")
                documentLoaded.value = true
            }
            is EditorMessage.EditorStatusChanged -> {
                editorStatusLine.value = message.payload.toString().take(120)
            }
            else -> Unit
        }
    }

    private fun welcomeDocumentMessage(): HostMessage.LoadDocument = HostMessage.LoadDocument(
        documentId = WELCOME_DOCUMENT_ID,
        revision = 0,
        payload = HostMessage.LoadDocument.LoadDocumentPayload(
            markdown = WELCOME_MARKDOWN,
            readOnly = false,
        ),
    )

    companion object {
        private const val TAG = "MainActivity"
        private const val WELCOME_DOCUMENT_ID = "welcome"
        private const val EDITOR_URL = "file:///android_asset/editor/index.html"
    }
}

@Composable
private fun TabStripPlaceholder(active: String, documentLoaded: Boolean) {
    val marker = if (documentLoaded) "●" else ""
    Text(
        text = "$marker $active",
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        style = MaterialTheme.typography.titleMedium,
    )
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
- [ ] 编辑闭环（进行中）

## Mermaid

```mermaid
graph LR
    A[打开文档] --> B[编辑]
    B --> C[导出]
```
""".trim()
