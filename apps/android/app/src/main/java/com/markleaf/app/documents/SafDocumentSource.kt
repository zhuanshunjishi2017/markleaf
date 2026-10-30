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

    override suspend fun rename(ref: DocumentRef, name: String): DocumentRef.Saf = withContext(Dispatchers.IO) {
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
     * @param documentDir 文档目录的 SAF document uri 字符串（即相对路径的基准目录）
     * @param relativePath 相对该目录的 POSIX 路径（已由 AssetResolver 归一化）
     */
    suspend fun readSiblingBytes(documentDir: String, relativePath: String): ByteArray? =
        withContext(Dispatchers.IO) {
            val documentUri = Uri.parse(documentDir)
            val documentId = runCatching { DocumentsContract.getDocumentId(documentUri) }
                .getOrNull() ?: return@withContext null
            val segments = SafDocumentPath.segments(relativePath)
            if (segments.isEmpty()) return@withContext null

            // 从传入目录自身出发逐段行走（caller 已给基准目录，不再取父）
            val finalId = SafDocumentPath.walkDocumentId(documentId, segments) { parentId, segment ->
                childDocumentsUri(documentUri, parentId)?.let { childrenUri ->
                    findChildDocumentId(childrenUri, segment)
                }
            } ?: return@withContext null
            val targetUri = childDocumentUri(documentUri, finalId)
            contentResolver.openInputStream(targetUri)?.use { stream -> stream.readBytes() }
        }

    private fun childDocumentsUri(documentUri: Uri, parentId: String): Uri? =
        if (documentUri.pathSegments.contains("tree")) {
            DocumentsContract.buildChildDocumentsUriUsingTree(documentUri, parentId)
        } else {
            documentUri.authority?.let { DocumentsContract.buildChildDocumentsUri(it, parentId) }
        }

    private fun childDocumentUri(documentUri: Uri, childId: String): Uri =
        if (documentUri.pathSegments.contains("tree")) {
            DocumentsContract.buildDocumentUriUsingTree(documentUri, childId)
        } else {
            DocumentsContract.buildDocumentUri(documentUri.authority, childId)
        }

    private fun findChildDocumentId(childrenUri: Uri, targetName: String): String? {
        val projection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
        )
        contentResolver.query(childrenUri, projection, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val name = cursor.getString(1) ?: continue
                if (name.equals(targetName, ignoreCase = true)) {
                    return cursor.getString(0)
                }
            }
        }
        return null
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

/** SAF 文档 ID 的纯路径规则，独立出来便于测试和避免 URI 字符串猜测。 */
internal object SafDocumentPath {
    fun parentDocumentId(documentId: String): String {
        val slash = documentId.lastIndexOf('/')
        val colon = documentId.indexOf(':')
        return when {
            slash > colon -> documentId.substring(0, slash)
            colon >= 0 -> documentId.substring(0, colon + 1)
            else -> documentId
        }
    }

    fun segments(relativePath: String): List<String> =
        relativePath.trim('/').split('/').filter { it.isNotEmpty() && it != "." && it != ".." }

    /**
     * 从 rootDocumentId 出发按段行走子文档 ID；任一段找不到返回 null。
     * 起点是传入的根本身（caller 负责给对基准目录），不再自动取父——
     * 回归护栏：曾从父目录起步导致从卷根查找、嵌套图片全 404。
     */
    fun walkDocumentId(
        rootDocumentId: String,
        segments: List<String>,
        findChild: (parentDocumentId: String, name: String) -> String?,
    ): String? {
        var current = rootDocumentId
        for (segment in segments) {
            current = findChild(current, segment) ?: return null
        }
        return current
    }

    fun parentDocumentUri(documentUri: Uri): Uri? {
        val documentId = runCatching { DocumentsContract.getDocumentId(documentUri) }.getOrNull() ?: return null
        val parentId = parentDocumentId(documentId)
        return if (documentUri.pathSegments.contains("tree")) {
            DocumentsContract.buildDocumentUriUsingTree(documentUri, parentId)
        } else {
            documentUri.authority?.let { DocumentsContract.buildDocumentUri(it, parentId) }
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
