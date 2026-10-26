package com.markleaf.app.data

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** TabState LRU 契约（spec §2 决策 2：上限 6 活跃 WebView，LRU 关闭最旧）。 */
class TabStateTest {

    @Test
    fun `activating moves tab to most recent`() {
        val state = TabState()
        state.open("a"); state.open("b"); state.open("c")
        state.activate("a")
        assertEquals(listOf("b", "c", "a"), state.lruOrder())
        assertEquals("a", state.activeId)
    }

    @Test
    fun `opening seventh tab evicts oldest`() {
        val state = TabState()
        for (id in listOf("a", "b", "c", "d", "e", "f")) state.open(id)
        val evicted = state.open("g")
        assertEquals("a", evicted)
        assertEquals(listOf("b", "c", "d", "e", "f", "g"), state.lruOrder())
    }

    @Test
    fun `close removes and reassigns active`() {
        val state = TabState()
        state.open("a"); state.open("b")
        state.activate("a")
        state.close("a")
        assertEquals("b", state.activeId)
    }

    @Test
    fun `memory trim keeps only active`() {
        val state = TabState()
        for (id in listOf("a", "b", "c")) state.open(id)
        state.activate("b")
        state.trimToActive()
        assertEquals(listOf("b"), state.lruOrder())
    }

    @Test
    fun `cannot close last tab`() {
        val state = TabState()
        state.open("only")
        state.close("only")
        assertEquals(listOf("only"), state.lruOrder())
    }
}

/** SettingsStore DataStore 读写契约。 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class SettingsStoreTest {

    @Test
    fun `defaults before first write`() = runBlocking {
        val store = SettingsStore(ApplicationProvider.getApplicationContext<Context>())
        store.clearAllForTest()
        val settings = store.readOnce()
        assertEquals("system", settings.themeMode)
        assertEquals("UTF-8", settings.defaultEncoding)
        assertEquals("dash", settings.bulletMarker)
        assertEquals(true, settings.showStatusBar)
    }

    @Test
    fun `write then read round trip`() = runBlocking {
        val store = SettingsStore(ApplicationProvider.getApplicationContext<Context>())
        store.clearAllForTest()
        store.update { it.copy(themeMode = "dark", defaultEncoding = "GBK") }
        val settings = store.readOnce()
        assertEquals("dark", settings.themeMode)
        assertEquals("GBK", settings.defaultEncoding)
    }

    @Test
    fun `recent files capped and deduplicated`() = runBlocking {
        val store = SettingsStore(ApplicationProvider.getApplicationContext<Context>())
        store.clearAllForTest()
        for (index in 1..15) {
            store.addRecentFile("doc-$index")
        }
        store.addRecentFile("doc-3")
        val settings = store.readOnce()
        assertEquals("doc-3", settings.recentFiles.first())
        assertEquals(10, settings.recentFiles.size)
        assertTrue(!settings.recentFiles.contains("doc-1"))
        assertTrue(!settings.recentFiles.contains("doc-5")) // 第 11 个被挤出
        assertTrue(settings.recentFiles.contains("doc-7")) // 保留最旧的是 doc-7
    }
}
