package com.markleaf.app.bridge

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 语义对齐 apps/windows/MarkLeaf/Editor/EditorSession.cs 的 Accept 守卫。 */
class EditorSessionStateTest {

    private fun state() = EditorSessionState().apply { startDocument("doc-1") }

    @Test
    fun `accepts message from same document`() {
        val s = state()
        assertTrue(s.accept(editorMessage("doc-1", revision = 5)))
        assertEquals(5L, s.confirmedRevision)
    }

    @Test
    fun `rejects message from foreign document`() {
        val s = state()
        assertFalse(s.accept(editorMessage("other", revision = 1)))
        assertEquals(0L, s.confirmedRevision)
    }

    @Test
    fun `rejects stale revision`() {
        val s = state()
        assertTrue(s.accept(editorMessage("doc-1", revision = 7)))
        assertFalse(s.accept(editorMessage("doc-1", revision = 6)))
    }

    @Test
    fun `accepts same revision twice`() {
        val s = state()
        assertTrue(s.accept(editorMessage("doc-1", revision = 3)))
        assertTrue(s.accept(editorMessage("doc-1", revision = 3)))
    }

    @Test
    fun `revision resets on new document - windows bug fixed`() {
        val s = state()
        s.accept(editorMessage("doc-1", revision = 100))
        // C# EditorSession 未重置 ConfirmedRevision 导致新文档消息被吞；
        // Kotlin 端 startDocument 必须重置（spec §3.2）。
        s.startDocument("doc-2", revision = 0)
        assertTrue(s.accept(editorMessage("doc-2", revision = 0)))
    }

    @Test
    fun `startDocument accepts explicit revision`() {
        val s = EditorSessionState()
        s.startDocument("doc-1", revision = 42)
        assertFalse(s.accept(editorMessage("doc-1", revision = 41)))
        assertTrue(s.accept(editorMessage("doc-1", revision = 42)))
    }

    // ---- requestId ----

    @Test
    fun `register generates id and completes with type match`() {
        val s = state()
        val id = s.registerRequest("snapshot")
        assertTrue(id.isNotEmpty())
        assertTrue(s.completeRequest(id, "snapshot"))
        // 完成后不可重复完成
        assertFalse(s.completeRequest(id, "snapshot"))
    }

    @Test
    fun `completeRequest rejects type mismatch`() {
        val s = state()
        val id = s.registerRequest("commandResult", "fixed-id")
        assertFalse(s.completeRequest("fixed-id", "snapshot"))
        assertTrue(s.completeRequest("fixed-id", "commandResult"))
    }

    @Test
    fun `completeRequest unknown id fails`() {
        val s = state()
        assertFalse(s.completeRequest("missing", "snapshot"))
    }

    private fun editorMessage(documentId: String, revision: Long): EditorMessage.DirtyChanged =
        EditorMessage.DirtyChanged(
            documentId = documentId,
            revision = revision,
            payload = EditorMessage.DirtyChanged.DirtyPayload(dirty = true),
        )
}
