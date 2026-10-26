package com.markleaf.app.bridge

import android.util.Log
import android.webkit.JavascriptInterface
import android.webkit.WebView

/**
 * 宿主↔编辑器消息桥（对应 macOS EditorSession / Windows EditorHostController）。
 *
 * - 宿主→前端：evaluateJavascript 执行 window.postMessage(JSON.parse('…'), '*'）
 *   与桌面端逐字同构；JSON 字符串按 JS 单引号字面量转义。
 * - 前端→宿主：native-shim.js（Android 版）把 window.chrome.webview.postMessage
 *   实现为 markleafHost.postMessage(JSON.stringify(msg))；本桥解析后经
 *   EditorSessionState 过滤再回调。
 *
 * 线程：evaluateJavascript 必须在主线程（WebView 约束）；JS 桥回调出现在
 * WebView 的 JS 线程，统一 post 回主线程。
 */
class EditorBridge(
    private val postScript: (String) -> Unit,
    private val session: EditorSessionState,
    private val onEditorMessage: (EditorMessage) -> Unit = {},
) {
    private val pendingOutbound = ArrayDeque<HostMessage>()
    private var editorReady = false
    private val mainHandler = android.os.Handler(android.os.Looper.getMainLooper())

    /** 注册 JS 接口到宿主 WebView（真实环境）；测试环境可跳过。 */
    fun attach(webView: WebView? = null) {
        webView?.addJavascriptInterface(BridgeReceiver(), JS_INTERFACE)
    }

    /**
     * 宿主→编辑器。ready 前缓存（对齐桌面时序：ready → applyStyles →
     * stylesApplied → loadDocument），ready 后依序 flush。
     */
    fun post(message: HostMessage) {
        checkMainThread()
        if (!editorReady) {
            pendingOutbound.addLast(message)
            return
        }
        postToEditor(message)
    }

    /**
     * JS 桥入口：前端→宿主。真实环境从 WebView JS 线程到达，切主线程后处理；
     * 已在主线程时同步处理（测试与宿主主线程调用即此路径）。
     */
    fun onEditorJson(json: String) {
        if (android.os.Looper.myLooper() == android.os.Looper.getMainLooper()) {
            handleEditorJson(json)
        } else {
            mainHandler.post { handleEditorJson(json) }
        }
    }

    private fun handleEditorJson(json: String) {
        val message = parseEditorMessage(json)
        if (message == null) {
            Log.w(TAG, "无法解析编辑器消息（type 未知或协议不符）: ${json.take(120)}")
            return
        }
        if (!session.accept(message)) {
            Log.w(
                TAG,
                "拒绝过期或外来编辑器消息: type=${message.type} doc=${message.documentId} " +
                    "rev=${message.revision} (expected doc=${session.documentId} rev=${session.confirmedRevision})",
            )
            return
        }
        if (message is EditorMessage.Ready) {
            editorReady = true
        }
        val deliver: (EditorMessage) -> Unit = { onEditorMessage(it) }
        deliver(message)
        if (message is EditorMessage.Ready) {
            while (pendingOutbound.isNotEmpty()) {
                postToEditor(pendingOutbound.removeFirst())
            }
        }
    }

    private fun postToEditor(message: HostMessage) {
        val json = encodeHostMessage(message)
        val script = "window.postMessage(JSON.parse('${escapeJsSingleQuoted(json)}'), '*')"
        postScript(script)
    }

    /** JS 单引号字面量转义：反斜杠、单引号、换行、回车、其他控制符。 */
    private fun escapeJsSingleQuoted(value: String): String = buildString(value.length + 8) {
        for (ch in value) {
            when (ch) {
                '\\' -> append("\\\\")
                '\'' -> append("\\'")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                else -> if (ch < ' ') append("\\u%04x".format(ch.code)) else append(ch)
            }
        }
    }

    private fun checkMainThread() {
        check(android.os.Looper.myLooper() == android.os.Looper.getMainLooper()) {
            "EditorBridge.post 必须在主线程调用（WebView 约束）"
        }
    }

    /** JS 桥接收器：native-shim.js 调用 markleafHost.postMessage(jsonString)。 */
    inner class BridgeReceiver {
        @JavascriptInterface
        fun postMessage(json: String) {
            onEditorJson(json)
        }
    }

    companion object {
        const val JS_INTERFACE = "markleafHost"
        private const val TAG = "EditorBridge"
    }
}
