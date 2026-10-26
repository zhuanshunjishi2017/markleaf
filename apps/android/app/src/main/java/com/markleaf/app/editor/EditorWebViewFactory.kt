package com.markleaf.app.editor

import android.content.Context
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient

/**
 * 编辑器 WebView 工厂：统一安全与渲染配置（spec §3.1/§7）。
 * WebView 只从 APK assets 加载编辑器页面，文件系统/内容提供器访问一律关闭；
 * 图片等文档资源经 shouldInterceptRequest 的 assets.local 拦截器供给。
 */
object EditorWebViewFactory {

    fun create(context: Context): WebView {
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
            // M1 阶段拦截器占位：assets.local 请求一律 404，Task 10 实装文档图片解析
        }
        return webView
    }
}
