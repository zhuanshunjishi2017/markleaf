package com.markleaf.app.documents

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.webkit.MimeTypeMap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * SAF 文档树实现（spec §4）：DocumentsContract 全操作。
 * 所有方法在 Dispatchers.IO；SecurityException（持久化权限被撤销）按 spec §8
 * 原样抛出，由 DocumentController 转为"重新授权 + 只读快照"流程。
 */
class SafDocumentSource(private val context: Context) : DocumentSource {

    private val contentResolver = context.contentResolver

    override suspend fun listChildren(folder: DocumentRef): List<DocumentEntry> = withContext(Dispatchers.IO) {
        val folderUri = folder.requireSaf().parsedUri
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(
            folderUri,
            DocumentsContract.getDocumentId(folderUri),
        )
        val entries = mutableListOf<DocumentEntry>()
        val projection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
        )
        contentResolver.query(childrenUri, projection, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val documentId = cursor.getString(0) ?: continue
                val name = cursor.getString(1) ?: continue
                val mime = cursor.getString(2) ?: ""
                val documentUri = DocumentsContract.buildDocumentUriUsingTree(folderUri, documentId)
                entries += DocumentEntry(
                    ref = DocumentRef.Saf(documentUri.toString()),
                    name = name,
                    isFolder = mime == DocumentsContract.Document.MIME_TYPE_DIR,
                )
            }
        } ?: throw SecurityException("无法列出 SAF 目录：$childrenUri")
        entries.sortedWith(compareByDescending<DocumentEntry> { it.isFolder }.thenBy { it.name.lowercase() })
    }

    override suspend fun read(ref: DocumentRef): DocumentBytes = withContext(Dispatchers.IO) {
        val uri = ref.requireSaf().parsedUri
        val bytes = contentResolver.openInputStream(uri)?.use { it.readBytes() }
            ?: throw SecurityException("无法读取 SAF 文档：$uri")
        DocumentBytes(bytes = bytes, encoding = DocumentCodings.detect(bytes))
    }

    override suspend fun write(ref: DocumentRef, bytes: ByteArray): Unit = withContext(Dispatchers.IO) {
        val uri = ref.requireSaf().parsedUri
        contentResolver.openOutputStream(uri, "wt")?.use { output ->
            output.write(bytes)
            output.flush()
        } ?: throw SecurityException("无法写入 SAF 文档：$uri")
    }

    override suspend fun rename(ref: DocumentRef, name: String): DocumentRef = withContext(Dispatchers.IO) {
        val uri = ref.requireSaf().parsedUri
        val renamed = DocumentsContract.renameDocument(contentResolver, uri, name)
        DocumentRef.Saf(renamed.toString())
    }

    override suspend fun delete(ref: DocumentRef): Unit = withContext(Dispatchers.IO) {
        val uri = ref.requireSaf().parsedUri
        if (!DocumentsContract.deleteDocument(contentResolver, uri)) {
            throw SecurityException("SAF 拒绝删除：$uri")
        }
    }

    override suspend fun createDocument(folder: DocumentRef, name: String): DocumentRef = withContext(Dispatchers.IO) {
        val folderUri = folder.requireSaf().parsedUri
        val created = DocumentsContract.createDocument(
            contentResolver,
            folderUri,
            "text/markdown",
            name,
        ) ?: throw SecurityException("SAF 拒绝创建文档：$folderUri")
        DocumentRef.Saf(created.toString())
    }

    override suspend fun createFolder(folder: DocumentRef, name: String): DocumentRef = withContext(Dispatchers.IO) {
        val folderUri = folder.requireSaf().parsedUri
        val created = DocumentsContract.createDocument(
            contentResolver,
            folderUri,
            DocumentsContract.Document.MIME_TYPE_DIR,
            name,
        ) ?: throw SecurityException("SAF 拒绝创建文件夹：$folderUri")
        DocumentRef.Saf(created.toString())
    }

    /**
     * 读取文档目录下的资源文件（图片等），供 assets.local 拦截器调用。
     * @param documentDir 文档目录的 SAF document uri 字符串
     * @param relativePath 相对授权根的 POSIX 路径（已由 AssetResolver 归一化）
     */
    suspend fun readSiblingBytes(documentDir: String, relativePath: String): ByteArray? =
        withContext(Dispatchers.IO) {
            val dirUri = Uri.parse(documentDir)
            val documentId = runCatching { DocumentsContract.getDocumentId(dirUri) }.getOrNull()
                ?: return@withContext null
            val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(dirUri, documentId)
            val projection = arrayOf(
                DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            )
            // 目标文件名 = 相对路径末段（AssetResolver 已保证路径在授权根内）
            val targetName = relativePath.substringAfterLast('/')
            contentResolver.query(childrenUri, projection, null, null, null)?.use { cursor ->
                while (cursor.moveToNext()) {
                    val name = cursor.getString(1) ?: continue
                    if (!name.equals(targetName, ignoreCase = true)) continue
                    val childUri = DocumentsContract.buildDocumentUriUsingTree(dirUri, cursor.getString(0))
                    return@withContext contentResolver.openInputStream(childUri)?.use { it.readBytes() }
                }
            }
            null
        }

    companion object {
        /** 持久化授权：打开文件夹后调用，重启后仍可访问（spec §4.3 前提）。 */
        fun takePersistablePermission(context: Context, treeUri: Uri) {
            val flags = android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or
                android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            context.contentResolver.takePersistableUriPermission(treeUri, flags)
        }
    }
}

/** DocumentRef 必须是 SAF 引用；否则为编程错误。 */
fun DocumentRef.requireSaf(): DocumentRef.Saf = this as? DocumentRef.Saf
    ?: throw IllegalArgumentException("预期 SAF 文档引用，实际为 $this")

/** 文档名是否为 Markdown（工作区树过滤 + 打开校验）。 */
fun isMarkdownName(name: String): Boolean {
    val extension = name.substringAfterLast('.', "").lowercase()
    return extension == "md" || extension == "markdown"
}

/** MIME 推断：SAF 创建时 text/markdown 兜底。 */
fun mimeForName(name: String): String {
    val extension = name.substringAfterLast('.', "").lowercase()
    return MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension) ?: "text/markdown"
}
