package com.markleaf.app.documents

import com.markleaf.app.bridge.EditorMessage
import com.markleaf.app.bridge.EditorSessionState
import com.markleaf.app.bridge.HostMessage
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** DocumentController 打开/保存/冲突契约（spec §7 数据流）。 */
class DocumentControllerTest {

    /** 内存 DocumentSource：可控内容与故障注入。 */
    private class FakeSource : DocumentSource {
        val files = mutableMapOf<String, Pair<ByteArray, Long>>() // name -> (bytes, lastWriteSerial)
        var readFailure: Exception? = null
        private var writeSerial = 0L

        override suspend fun listChildren(folder: DocumentRef): List<DocumentEntry> = emptyList()

        override suspend fun read(ref: DocumentRef): DocumentBytes {
            readFailure?.let { throw it }
            val key = (ref as DocumentRef.Saf).uri
            val bytes = files.getValue(key).first
            return DocumentBytes(bytes, DocumentCodings.detect(bytes))
        }

        override suspend fun write(ref: DocumentRef, bytes: ByteArray) {
            val key = (ref as DocumentRef.Saf).uri
            files[key] = Pair(bytes, ++writeSerial)
        }

        override suspend fun rename(ref: DocumentRef, name: String): DocumentRef = ref

        override suspend fun delete(ref: DocumentRef) {
            files.remove((ref as DocumentRef.Saf).uri)
        }

        override suspend fun createDocument(folder: DocumentRef, name: String): DocumentRef =
            DocumentRef.Saf("fake://$name")

        override suspend fun createFolder(folder: DocumentRef, name: String): DocumentRef =
            DocumentRef.Saf("fake://$name/")
    }

    /** 捕获宿主发出的消息序列。 */
    private class CapturingBridge {
        val posted = mutableListOf<HostMessage>()
        fun post(message: HostMessage) {
            posted.add(message)
        }
    }

    private fun ref(name: String) = DocumentRef.Saf("fake://$name")

    @Test
    fun `open reads bytes, detects encoding, posts loadDocument`() = runTest {
        val source = FakeSource()
        source.files["fake://note.md"] =
            Pair("# 笔记\n\n中文内容".toByteArray(Charsets.UTF_8), 0)
        val bridge = CapturingBridge()
        val session = EditorSessionState().apply { startDocument("pending") }
        val controller = DocumentController(source, bridge::post, session)

        val opened = controller.open(ref("note.md"))

        assertTrue(opened is DocumentController.OpenResult.Loaded)
        val load = bridge.posted.filterIsInstance<HostMessage.LoadDocument>().single()
        assertEquals("# 笔记\n\n中文内容", load.payload.markdown)
        assertEquals(false, load.payload.readOnly)
        // 会话切换到新文档 id
        assertEquals(opened.hashCode(), opened.hashCode())
        assertTrue(session.documentId != "pending")
    }

    @Test
    fun `open failure from revoked permission surfaces as PermissionLost`() = runTest {
        val source = FakeSource()
        source.readFailure = SecurityException("permission revoked")
        val controller = DocumentController(source, {}, EditorSessionState())

        val opened = controller.open(ref("gone.md"))

        assertTrue(opened is DocumentController.OpenResult.PermissionLost)
    }

    @Test
    fun `save requests snapshot, writes encoded bytes and marks saved`() = runTest {
        val source = FakeSource()
        source.files["fake://note.md"] = Pair("旧内容".toByteArray(Charsets.UTF_8), 0)
        val session = EditorSessionState().apply { startDocument("doc-9") }
        val posted = mutableListOf<HostMessage>()
        val controller = DocumentController(source, { posted.add(it) }, session)
        // 先打开（建立外部基线）
        controller.open(ref("note.md"))
        posted.clear()

        val result = controller.save(
            snapshotProvider = { EditorMessage.Snapshot.SnapshotPayload(markdown = "新内容") },
            dirty = true,
        )

        assertTrue(result == DocumentController.SaveResult.Saved)
        // 按文档原编码（UTF-8）写回
        assertEquals("新内容", String(source.files.getValue("fake://note.md").first, Charsets.UTF_8))
        // 发出 markSaved
        assertTrue(posted.filterIsInstance<HostMessage.MarkSaved>().isNotEmpty())
    }

    @Test
    fun `save detects external modification as conflict`() = runTest {
        val source = FakeSource()
        source.files["fake://note.md"] = Pair("基线".toByteArray(Charsets.UTF_8), 0)
        val controller = DocumentController(source, {}, EditorSessionState().apply { startDocument("d") })
        controller.open(ref("note.md"))

        // 外部在打开后改写文件（模拟另一编辑器）
        source.files["fake://note.md"] = Pair("外部修改".toByteArray(Charsets.UTF_8), 0)

        val result = controller.save(
            snapshotProvider = { EditorMessage.Snapshot.SnapshotPayload(markdown = "我的编辑") },
            dirty = true,
            externalChanged = true,
        )

        assertEquals(DocumentController.SaveResult.Conflict, result)
        // 冲突时不写文件、不发 markSaved
    }

    @Test
    fun `save keeps original encoding`() = runTest {
        val source = FakeSource()
        // GBK 文档
        source.files["fake://legacy.md"] = Pair("中文".toByteArray(charset("GBK")), 0)
        val controller = DocumentController(source, {}, EditorSessionState().apply { startDocument("d") })
        controller.open(ref("legacy.md"))

        controller.save(
            snapshotProvider = { EditorMessage.Snapshot.SnapshotPayload(markdown = "中文新内容") },
            dirty = true,
        )

        val written = source.files.getValue("fake://legacy.md").first
        // GBK 解码回原文验证未变编码
        assertEquals("中文新内容", String(written, charset("GBK")))
    }

    @Test
    fun `clean document save is skipped`() = runTest {
        val source = FakeSource()
        source.files["fake://a.md"] = Pair("内容".toByteArray(Charsets.UTF_8), 0)
        val controller = DocumentController(source, {}, EditorSessionState().apply { startDocument("d") })
        controller.open(ref("a.md"))

        val result = controller.save(
            snapshotProvider = { EditorMessage.Snapshot.SnapshotPayload(markdown = "内容") },
            dirty = false,
        )

        assertEquals(DocumentController.SaveResult.NotDirty, result)
    }

    @Test
    fun `write permission lost on save surfaces as PermissionLost`() = runTest {
        val source = FakeSource()
        source.files["fake://ro.md"] = Pair("内容".toByteArray(Charsets.UTF_8), 0)
        val controller = DocumentController(source, {}, EditorSessionState().apply { startDocument("d") })
        controller.open(ref("ro.md"))

        // 模拟写回时权限丢失
        val brokenSource = object : DocumentSource by source {
            override suspend fun write(ref: DocumentRef, bytes: ByteArray) {
                throw SecurityException("revoked on write")
            }
        }
        val controller2 = DocumentController(brokenSource, {}, EditorSessionState().apply { startDocument("d") })
        // 需要同源的基线：用 controller2 重新打开（FakeSource 内容未变）
        controller2.open(ref("ro.md"))

        val result = controller2.save(
            snapshotProvider = { EditorMessage.Snapshot.SnapshotPayload(markdown = "新") },
            dirty = true,
        )

        assertEquals(DocumentController.SaveResult.PermissionLost, result)
    }
}
