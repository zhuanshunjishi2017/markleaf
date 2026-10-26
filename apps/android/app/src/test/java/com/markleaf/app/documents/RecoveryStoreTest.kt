package com.markleaf.app.documents

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class RecoveryStoreTest {

    private val context: Context = ApplicationProvider.getApplicationContext()

    @Test
    fun `write and consume round trip`() {
        val store = RecoveryStore(context)
        val ref = DocumentRef.Saf("content://tests/tree/primary%3ADocs/document/primary%3ADocs%2Fn.md")
        store.writePending("doc-1", ref, "# 草稿\n\n未保存内容")

        assertEquals(listOf("doc-1"), store.pendingIds())
        val entry = store.consume("doc-1")!!
        assertEquals("doc-1", entry.documentId)
        assertEquals(ref, entry.ref)
        assertEquals("# 草稿\n\n未保存内容", entry.markdown)
        assertEquals(ref, documentRefFromStableString(documentRefToStableString(entry.ref)))
    }

    @Test
    fun `discard removes pending entry`() {
        val store = RecoveryStore(context)
        val ref = DocumentRef.Saf("content://tests/document/x")
        store.writePending("doc-2", ref, "text")
        store.discard("doc-2")
        assertNull(store.consume("doc-2"))
        assertEquals(emptyList<String>(), store.pendingIds())
    }

    @Test
    fun `consume missing id returns null`() {
        val store = RecoveryStore(context)
        assertNull(store.consume("never-written"))
    }
}
