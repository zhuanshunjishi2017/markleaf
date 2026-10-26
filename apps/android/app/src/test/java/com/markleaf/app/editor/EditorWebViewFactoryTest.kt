package com.markleaf.app.editor

import android.webkit.WebView
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** WebView 配置契约（spec §3.1/§7）：安全与渲染相关 settings 全项。 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class EditorWebViewFactoryTest {

    private fun settingsOf(webView: WebView) = webView.settings

    @Test
    fun `configures javascript and dom storage`() {
        val webView = EditorWebViewFactory.create(ApplicationProvider.getApplicationContext())
        val settings = settingsOf(webView)
        assertTrue(settings.javaScriptEnabled)
        assertTrue(settings.domStorageEnabled)
    }

    @Test
    fun `disables local file access and mixed content`() {
        val webView = EditorWebViewFactory.create(ApplicationProvider.getApplicationContext())
        val settings = settingsOf(webView)
        // WebView 只从 APK assets 加载，文件系统访问一律关闭
        assertFalse(settings.allowFileAccess)
        assertFalse(settings.allowContentAccess)
    }

    @Test
    fun `keeps default text zoom for editor internal scaling`() {
        val webView = EditorWebViewFactory.create(ApplicationProvider.getApplicationContext())
        // 缩放由前端 zoom 命令处理；系统 textZoom 固定 100 避免双重缩放
        assertEquals(100, settingsOf(webView).textZoom)
    }
}
