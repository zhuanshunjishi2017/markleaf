package com.markleaf.app.editor

import android.content.Context
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import com.markleaf.app.documents.AssetResolver

/**
 * 编辑器 WebView 工厂：统一安全与渲染配置（spec §3.1/§7）。
 * WebView 只从 APK assets 加载编辑器页面，文件系统/内容提供器访问一律关闭；
 * 图片等文档资源经 shouldInterceptRequest 的 assets.local 拦截器供给。
 */
object EditorWebViewFactory {

    fun create(context: Context, assetResolver: AssetResolver? = null, documentDir: () -> String = { "" }): WebView {
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
            // assets.local 拦截器：文档图片从这里供给（桌面端 AssetSchemeHandler 同构）。
            // resolver 为空或路径越界时返回 null → 走正常网络栈 → 404。
            override fun shouldInterceptRequest(
                view: WebView,
                request: WebResourceRequest,
            ): WebResourceResponse? {
                val resolver = assetResolver ?: return null
                val url = request.url
                if (url.host != "assets.local" || url.path != "/image") {
                    return null
                }
                val path = url.getQueryParameter("path") ?: return null
                val bytes = resolver.resolve(path, documentDir()) ?: return notFound()
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

            private fun notFound() = WebResourceResponse("text/plain", null, 404, "Not Found", null, byteArrayOf().inputStream())
        }
        return webView
    }
}
