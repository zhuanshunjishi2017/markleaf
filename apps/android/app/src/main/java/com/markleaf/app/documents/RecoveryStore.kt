package com.markleaf.app.documents

import android.content.Context
import org.json.JSONObject
import java.io.File

/**
 * 未保存恢复快照（spec §4.3）：onPause/关标签/低内存时写入 app 私有目录，
 * 启动时发现即弹恢复对话框；正常保存或用户放弃后删除。
 */
class RecoveryStore(context: Context) {

    private val directory = File(context.filesDir, "recovery").apply { mkdirs() }

    data class Entry(
        val documentId: String,
        /** 未命名文档（欢迎文档等）无 SAF 引用：恢复时直接载入内容。 */
        val ref: DocumentRef.Saf?,
        val markdown: String,
        val savedAt: Long,
    )

    fun writePending(documentId: String, ref: DocumentRef.Saf?, markdown: String) {
        val payload = JSONObject().apply {
            put("documentId", documentId)
            if (ref != null) put("ref", documentRefToStableString(ref))
            put("markdown", markdown)
            put("savedAt", System.currentTimeMillis())
        }
        fileFor(documentId).writeText(payload.toString())
    }

    fun pendingIds(): List<String> =
        directory.listFiles { file -> file.isFile && file.name.endsWith(".json") }
            ?.map { it.name.removeSuffix(".json").hexToDocumentId() }
            ?.sorted()
            ?: emptyList()

    fun consume(documentId: String): Entry? {
        val file = fileFor(documentId)
        if (!file.exists()) {
            return null
        }
        return runCatching {
            val payload = JSONObject(file.readText())
            val ref = if (payload.has("ref")) {
                documentRefFromStableString(payload.getString("ref")) as? DocumentRef.Saf
            } else {
                null
            }
            Entry(
                documentId = payload.getString("documentId"),
                ref = ref,
                markdown = payload.getString("markdown"),
                savedAt = payload.getLong("savedAt"),
            )
        }.getOrNull()
    }

    fun discard(documentId: String) {
        fileFor(documentId).delete()
    }

    private fun fileFor(documentId: String): File =
        File(directory, "${documentId.toByteArray().toHex()}.json")

    private fun ByteArray.toHex(): String =
        joinToString("") { "%02x".format(it) }

    private fun String.hexToDocumentId(): String =
        chunked(2).map { it.toInt(16).toByte() }.toByteArray().decodeToString()
}
