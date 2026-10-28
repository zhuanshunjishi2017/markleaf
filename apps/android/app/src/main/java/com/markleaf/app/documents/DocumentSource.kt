package com.markleaf.app.documents

import java.nio.charset.Charset

import android.net.Uri

/**
 * 文档来源抽象（spec §4.1）：把“文件从哪来”与 UI/控制器隔离。
 * 第一版唯一实现 SafDocumentSource；远程源（SMB/WebDAV）未来实现同一接口。
 */
sealed interface DocumentRef {
    /** SAF tree/document URI（content://）。 */
    data class Saf(val uri: String) : DocumentRef
}

fun documentRefToStableString(ref: DocumentRef): String = when (ref) {
    is DocumentRef.Saf -> "saf:${ref.uri}"
}

fun documentRefFromStableString(value: String): DocumentRef? {
    if (!value.startsWith("saf:")) {
        return null
    }
    val uri = value.removePrefix("saf:")
    if (uri.isEmpty()) {
        return null
    }
    return DocumentRef.Saf(uri)
}

/** 目录项（工作区树/列表展示）。 */
data class DocumentEntry(
    val ref: DocumentRef,
    val name: String,
    val isFolder: Boolean,
)

/** 读取的文档字节与探测出的编码。 */
data class DocumentBytes(
    val bytes: ByteArray,
    val encoding: EncodingInfo,
)

/**
 * 编码探测结果：IANA 名 + 是否 BOM。与桌面端 DocumentEncodingPolicy 对齐
 * （UTF-8/UTF-8 BOM/UTF-16LE/UTF-16BE/GBK）。
 */
data class EncodingInfo(val name: String, val hasBom: Boolean)

/** 编解码失败的显式错误（调用方呈现“无法识别的编码”对话框）。 */
class EncodingUnavailableException(message: String) : IllegalArgumentException(message)

interface DocumentSource {
    suspend fun listChildren(folder: DocumentRef): List<DocumentEntry>
    suspend fun read(ref: DocumentRef): DocumentBytes
    suspend fun write(ref: DocumentRef, bytes: ByteArray)
    suspend fun rename(ref: DocumentRef, name: String): DocumentRef.Saf
    suspend fun delete(ref: DocumentRef)
    suspend fun createDocument(folder: DocumentRef, name: String): DocumentRef
    suspend fun createFolder(folder: DocumentRef, name: String): DocumentRef
}

/**
 * 编码探测与编解码（Kotlin 注入 document-kernel 的 OS 原语，spec §2 决策 4）。
 * 纯 JVM 逻辑，Robolectric/单测直接覆盖。
 */
object DocumentCodings {

    private val UTF8_BOM = byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte())
    private val UTF16LE_BOM = byteArrayOf(0xFF.toByte(), 0xFE.toByte())
    private val UTF16BE_BOM = byteArrayOf(0xFE.toByte(), 0xFF.toByte())

    fun detect(bytes: ByteArray): EncodingInfo {
        if (bytes.size >= 3 && bytes[0] == UTF8_BOM[0] && bytes[1] == UTF8_BOM[1] && bytes[2] == UTF8_BOM[2]) {
            return EncodingInfo("UTF-8", hasBom = true)
        }
        if (bytes.size >= 2 && bytes[0] == UTF16LE_BOM[0] && bytes[1] == UTF16LE_BOM[1]) {
            return EncodingInfo("UTF-16LE", hasBom = true)
        }
        if (bytes.size >= 2 && bytes[0] == UTF16BE_BOM[0] && bytes[1] == UTF16BE_BOM[1]) {
            return EncodingInfo("UTF-16BE", hasBom = true)
        }
        if (isValidUtf8(bytes)) {
            return EncodingInfo("UTF-8", hasBom = false)
        }
        // 非 UTF-8 的本地文本：GBK 是中文环境最常见回退（与桌面端探测链一致）。
        return EncodingInfo("GBK", hasBom = false)
    }

    fun decode(bytes: ByteArray, encoding: EncodingInfo): String {
        val charset = charsetFor(encoding.name)
        val text = String(stripBom(bytes, encoding), charset)
        return text
    }

    fun encode(text: String, encodingName: String): ByteArray {
        val charset = charsetFor(encodingName)
        return text.toByteArray(charset)
    }

    private fun charsetFor(name: String): Charset {
        return runCatching { charset(name) }.getOrElse { Charsets.UTF_8 }
    }

    private fun stripBom(bytes: ByteArray, encoding: EncodingInfo): ByteArray {
        if (!encoding.hasBom) {
            return bytes
        }
        return when (encoding.name) {
            "UTF-8" -> bytes.copyOfRange(3, bytes.size)
            "UTF-16LE", "UTF-16BE" -> bytes.copyOfRange(2, bytes.size)
            else -> bytes
        }
    }

    /** 严格 UTF-8 校验：多字节序列的连续性/续字节模式必须合法（对齐桌面端探测）。 */
    private fun isValidUtf8(bytes: ByteArray): Boolean {
        var index = 0
        val length = bytes.size
        while (index < length) {
            val first = bytes[index].toInt() and 0xFF
            val continuationCount = when {
                first < 0x80 -> 0
                first in 0xC2..0xDF -> 1
                first in 0xE0..0xEF -> 2
                first in 0xF0..0xF4 -> 3
                else -> return false
            }
            if (index + continuationCount >= length) {
                return false
            }
            for (offset in 1..continuationCount) {
                val continuation = bytes[index + offset].toInt() and 0xFF
                if (continuation !in 0x80..0xBF) {
                    return false
                }
            }
            index += continuationCount + 1
        }
        return true
    }
}

/** SAF 引用的 Uri 访问（避免把 android.net.Uri 泄漏进纯 JVM 测试）。 */
val DocumentRef.Saf.parsedUri: Uri
    get() = Uri.parse(uri)
