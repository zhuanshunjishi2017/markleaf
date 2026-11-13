package com.markleaf.app.export

import android.webkit.WebView
import com.markleaf.app.editor.EditorWebViewFactory
import kotlinx.coroutines.delay
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeout
import kotlin.coroutines.resume

/** Loads the standalone export document, never the live editing surface. Main thread only. */
internal suspend fun WebView.loadExportDocument(html: String) {
    settings.javaScriptEnabled = true
    settings.textZoom = 100
    settings.loadWithOverviewMode = true
    settings.useWideViewPort = true
    loadDataWithBaseURL(EditorWebViewFactory.EDITOR_URL, html, "text/html", "utf-8", null)
    withTimeout(30_000) {
        while (evaluateExportScript("""
            Boolean(document.getElementById('export-root') && document.readyState === 'complete' &&
              (!document.fonts || document.fonts.status === 'loaded') &&
              Array.from(document.images).every(function(image) { return image.complete; }))
        """.trimIndent()) != "true") {
            delay(40)
        }
    }
    evaluateExportScript("window.__markleafFitMath && window.__markleafFitMath(); true")
}

internal suspend fun WebView.evaluateExportScript(script: String): String =
    suspendCancellableCoroutine { continuation ->
        evaluateJavascript(script) { result ->
            if (continuation.isActive) continuation.resume(result)
        }
    }
