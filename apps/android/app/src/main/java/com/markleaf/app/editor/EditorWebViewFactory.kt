package com.markleaf.app.editor

import android.content.Context
import android.util.Log
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import com.markleaf.app.documents.AssetResolver

/**
 * 编辑器 WebView 工厂：统一安全与渲染配置（spec §3.1/§7）。
 *
 * 页面与资源全部经 `https://assets.local/` 拦截器从 APK assets 供给：
 * - ES module 脚本（type=module crossorigin）在 file:// 页面会被 CORS 拦截
 *   （origin null），必须挂到 https 自定义源下（对齐 Windows 的 editor.local）
 * - `/editor/...` → APK assets（编辑器页面与内核分包）
 * - `/image?path=...` → 文档图片（AssetResolver 经 SAF 解析）
 */
object EditorWebViewFactory {

    private const val TAG = "EditorWebViewFactory"

    private fun plainNotFound() = WebResourceResponse("text/plain", null, 404, "Not Found", null, byteArrayOf().inputStream())

    const val EDITOR_ORIGIN = "https://assets.local"
    const val EDITOR_URL = "$EDITOR_ORIGIN/editor/index.html"

    fun create(
        context: Context,
        assetResolver: AssetResolver? = null,
        documentDir: () -> String = { "" },
        onRenderProcessGone: (WebView) -> Boolean = { false },
    ): WebView {
        // 调试包开放 WebView 远程检查（chrome://inspect），正式包不受影响
        if (context.applicationInfo.flags and android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE != 0) {
            WebView.setWebContentsDebuggingEnabled(true)
        }
        val webView = WebView(context)
        webView.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            allowFileAccess = false
            allowContentAccess = false
            mediaPlaybackRequiresUserGesture = false
            cacheMode = WebSettings.LOAD_DEFAULT
            // 缩放由前端 zoom 命令统一处理；系统 textZoom 固定避免双重缩放
            textZoom = 100
        }
        webView.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(
                view: WebView,
                request: WebResourceRequest,
            ): WebResourceResponse? {
                val url = request.url
                if (url.host != "assets.local") {
                    return null
                }
                return when {
                    // 编辑器自身资产：APK assets 静态供流
                    url.path?.startsWith("/editor/") == true -> serveEditorAsset(context, url.path!!)
                    // katex-lazy 懒加载器用根级绝对路径请求 KaTeX 布局 CSS，
                    // 缺失会导致 KaTeX 渲染崩坏 + MathML 无障碍副本可见。
                    url.path == "/katex.min.css" -> serveEditorAsset(context, "/kernel/katex.min.css")
                    // 文档图片：SAF 解析（resolver 为空或越界时 404）
                    url.path == "/image" -> {
                        val resolver = assetResolver
                        val path = url.getQueryParameter("path")
                        if (resolver == null || path == null) {
                            plainNotFound()
                        } else {
                            serveImage(resolver.resolve(path, documentDir()), path)
                        }
                    }
                    else -> null
                }
            }

            override fun onRenderProcessGone(view: WebView, detail: android.webkit.RenderProcessGoneDetail): Boolean =
                onRenderProcessGone(view)

            private fun notFound() = WebResourceResponse("text/plain", null, 404, "Not Found", null, byteArrayOf().inputStream())
        }
        return webView
    }

    /** APK assets → 响应。MIME 按扩展名；未命中 404。 */
    private fun serveEditorAsset(context: Context, path: String): WebResourceResponse? {
        // 剥离前导斜杠（调用方可能传 /kernel/... 带斜杠路径，拼出双斜杠会找不到资产）
        val relative = path.removePrefix("/editor/").trimStart('/')
        val mime = when {
            relative.endsWith(".html", true) -> "text/html"
            relative.endsWith(".js", true) -> "text/javascript"
            relative.endsWith(".css", true) -> "text/css"
            relative.endsWith(".json", true) -> "application/json"
            relative.endsWith(".svg", true) -> "image/svg+xml"
            relative.endsWith(".woff2", true) -> "font/woff2"
            relative.endsWith(".png", true) -> "image/png"
            else -> "application/octet-stream"
        }
        val stream = try {
            context.assets.open("editor/$relative")
        } catch (exception: Exception) {
            Log.w(TAG, "editor asset 未命中: $relative")
            return plainNotFound()
        }
        return WebResourceResponse(mime, null, stream)
    }

    private fun serveImage(bytes: ByteArray?, path: String): WebResourceResponse {
        if (bytes == null) {
            return plainNotFound()
        }
        val mime = when {
            path.endsWith(".png", ignoreCase = true) -> "image/png"
            path.endsWith(".jpg", true) || path.endsWith(".jpeg", true) -> "image/jpeg"
            path.endsWith(".gif", true) -> "image/gif"
            path.endsWith(".webp", true) -> "image/webp"
            path.endsWith(".svg", true) -> "image/svg+xml"
            else -> "application/octet-stream"
        }
        return WebResourceResponse(mime, null, bytes.inputStream())
    }
}
