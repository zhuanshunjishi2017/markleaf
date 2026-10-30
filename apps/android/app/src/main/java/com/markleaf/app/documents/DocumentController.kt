package com.markleaf.app.documents

import com.markleaf.app.bridge.EditorMessage
import com.markleaf.app.bridge.EditorSessionState
import com.markleaf.app.bridge.HostMessage
import java.util.UUID

/**
 * 文档打开/保存/恢复的编排（spec §4.3/§7）。
 * IO 全部经 DocumentSource（内部 Dispatchers.IO）；本类方法为 suspend，
 * 由 Activity/ViewModel 在协程中调用。
 *
 * @param post 宿主→编辑器消息出口（EditorBridge.post）
 */
class DocumentController(
    private val source: DocumentSource,
    private val post: (HostMessage) -> Unit,
    private val session: EditorSessionState,
) {
    /** 当前文档引用与打开时的外部基线（冲突检测）。 */
    private var currentRef: DocumentRef.Saf? = null
    private var baselineBytes: ByteArray? = null
    private var currentEncoding: EncodingInfo? = null
    private var currentDocumentId: String = ""

    sealed interface OpenResult {
        data class Loaded(val documentId: String, val encoding: EncodingInfo, val name: String) : OpenResult
        data object PermissionLost : OpenResult
        data class Failed(val message: String) : OpenResult
    }

    sealed interface SaveResult {
        data object Saved : SaveResult
        data object NotDirty : SaveResult
        data object Conflict : SaveResult
        data object ReadOnly : SaveResult
        data object PermissionLost : SaveResult
        data class Failed(val message: String) : SaveResult
    }

    val activeRef: DocumentRef.Saf?
        get() = currentRef

    val activeEncoding: EncodingInfo?
        get() = currentEncoding

    /** 状态栏显示名：UTF-8 BOM 变体单独标注。 */
    val activeEncodingName: String
        get() = currentEncoding?.let { if (it.hasBom && it.name == "UTF-8") "UTF-8 BOM" else it.name } ?: "UTF-8"

    /** 切换保存写回编码（编码对话框选定后调用）。 */
    fun setEncoding(info: EncodingInfo) {
        currentEncoding = info
    }

    private var currentNewline: String = "LF"

    val activeNewline: String
        get() = currentNewline

    /** 切换保存写回换行符（LF / CRLF）。 */
    fun setNewline(style: String) {
        currentNewline = style
    }

    private fun applyNewline(markdown: String): String = when (currentNewline) {
        "CRLF" -> markdown.replace("\r\n", "\n").replace("\n", "\r\n")
        "LF" -> markdown.replace("\r\n", "\n")
        else -> markdown
    }

    private fun bomFor(name: String): ByteArray = when (name) {
        "UTF-8" -> byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte())
        "UTF-16LE" -> byteArrayOf(0xFF.toByte(), 0xFE.toByte())
        "UTF-16BE" -> byteArrayOf(0xFE.toByte(), 0xFF.toByte())
        else -> ByteArray(0)
    }

    /**
     * 打开文档：读字节 → 探测编码 → 解码 → 新 documentId → loadDocument。
     * 会话状态切换到新 id（revision 从 0 重新计——Windows 端 stale bug 的修正路径）。
     */
    private var currentReadOnly = false

    /** 当前文档是否只读（只读打开菜单的勾选与置灰依据）。 */
    val activeReadOnly: Boolean
        get() = currentReadOnly

    suspend fun open(ref: DocumentRef.Saf, readOnly: Boolean = false): OpenResult = try {
        val documentBytes = source.read(ref)
        val markdown = DocumentCodings.decode(documentBytes.bytes, documentBytes.encoding)
        val documentId = "doc-" + UUID.randomUUID().toString().take(8)
        session.startDocument(documentId, revision = 0)
        post(
            HostMessage.LoadDocument(
                documentId = documentId,
                revision = 0,
                payload = HostMessage.LoadDocument.LoadDocumentPayload(
                    markdown = markdown,
                    readOnly = readOnly,
                    documentPath = ref.uri,
                ),
            ),
        )
        currentRef = ref
        baselineBytes = documentBytes.bytes
        currentEncoding = documentBytes.encoding
        currentReadOnly = readOnly
        currentNewline = if (markdown.contains("\r\n")) "CRLF" else "LF"
        currentDocumentId = documentId
        OpenResult.Loaded(documentId, documentBytes.encoding, ref.uri.substringAfterLast('/'))
    } catch (exception: SecurityException) {
        OpenResult.PermissionLost
    } catch (exception: Exception) {
        OpenResult.Failed(exception.message ?: exception.javaClass.simpleName)
    }

    /** 新建文档：清引用与探测状态，会话沿用当前 documentId，内容由宿主下发。 */
    fun startNewDocument() {
        currentRef = null
        baselineBytes = null
        currentEncoding = null
        currentReadOnly = false
    }

    /** 另存为/重命名后收养新引用（基线清空：下次保存全量写入新文件）。
     *  另存为产生的是可写副本，只读会话态一并解除。 */
    fun adoptRef(ref: DocumentRef.Saf) {
        currentRef = ref
        baselineBytes = null
        currentReadOnly = false
    }

    /** 会话只读态切换（视图菜单只读模式开关调用），与打开时的只读态同源。 */
    fun setReadOnly(value: Boolean) {
        currentReadOnly = value
    }

    /**
     * 保存：dirty 时 requestSnapshot→snapshot→编码写回→markSaved。
     * externalChanged（保存前宿主重读文件比对哈希的结论）为真时返回 Conflict，
     * 由调用方弹冲突对话框（覆盖/另存）。
     */
    suspend fun save(
        snapshotProvider: suspend () -> EditorMessage.Snapshot.SnapshotPayload,
        dirty: Boolean,
        externalChanged: Boolean = false,
    ): SaveResult {
        if (!dirty) {
            return SaveResult.NotDirty
        }
        if (currentReadOnly) {
            return SaveResult.ReadOnly
        }
        val ref = currentRef ?: return SaveResult.Failed("无活动文档")
        if (externalChanged) {
            return SaveResult.Conflict
        }
        return try {
            val payload = snapshotProvider()
            val encoding = currentEncoding ?: EncodingInfo("UTF-8", hasBom = false)
            val normalized = applyNewline(payload.markdown)
            val encoded = DocumentCodings.encode(normalized, encoding.name)
            val bytes = if (encoding.hasBom) bomFor(encoding.name) + encoded else encoded
            source.write(ref, bytes)
            baselineBytes = bytes
            post(HostMessage.MarkSaved(documentId = currentDocumentId, revision = session.confirmedRevision))
            SaveResult.Saved
        } catch (exception: SecurityException) {
            SaveResult.PermissionLost
        } catch (exception: Exception) {
            SaveResult.Failed(exception.message ?: exception.javaClass.simpleName)
        }
    }

    /**
     * 保存前冲突检测：读当前文件与打开时基线比对（内容级，非 mtime）。
     * 读失败（如权限撤销）不阻塞保存——交给写路径的错误处理。
     */
    suspend fun detectExternalChange(): Boolean {
        val ref = currentRef ?: return false
        val baseline = baselineBytes ?: return false
        return try {
            val current = source.read(ref).bytes
            !current.contentEquals(baseline)
        } catch (exception: SecurityException) {
            false
        }
    }

    /** 标签关闭/应用切后台时写恢复快照（markdown 由调用方从最新 snapshot 取）。
     *  未命名文档（无 SAF 引用）也进恢复链路：恢复时直接载入内容。 */
    fun writeRecovery(store: RecoveryStore, markdown: String, dirty: Boolean) {
        if (!dirty) {
            store.discard(currentDocumentId)
            return
        }
        store.writePending(currentDocumentId, currentRef, markdown)
    }

    fun discardRecovery(store: RecoveryStore) {
        if (currentDocumentId.isNotEmpty()) {
            store.discard(currentDocumentId)
        }
    }
}
