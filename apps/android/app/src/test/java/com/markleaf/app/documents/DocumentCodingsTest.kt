package com.markleaf.app.documents

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** DocumentCodings 编码探测契约（与桌面 DocumentEncodingPolicy 对齐）。 */
class DocumentCodingsTest {

    @Test
    fun `detects utf-8 without bom`() {
        val bytes = "第一行正文 with ascii".toByteArray(Charsets.UTF_8)
        val info = DocumentCodings.detect(bytes)
        assertEquals("UTF-8", info.name)
        assertTrue(info.hasBom.not())
    }

    @Test
    fun `detects utf-8 bom`() {
        val bytes = byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte()) +
            "正文".toByteArray(Charsets.UTF_8)
        val info = DocumentCodings.detect(bytes)
        assertEquals("UTF-8", info.name)
        assertTrue(info.hasBom)
    }

    @Test
    fun `detects utf-16le bom`() {
        val bytes = byteArrayOf(0xFF.toByte(), 0xFE.toByte()) +
            "正文".toByteArray(Charsets.UTF_16LE)
        val info = DocumentCodings.detect(bytes)
        assertEquals("UTF-16LE", info.name)
    }

    @Test
    fun `detects utf-16be bom`() {
        val bytes = byteArrayOf(0xFE.toByte(), 0xFF.toByte()) +
            "正文".toByteArray(Charsets.UTF_16BE)
        val info = DocumentCodings.detect(bytes)
        assertEquals("UTF-16BE", info.name)
    }

    @Test
    fun `detects gbk for non-utf8 cjk bytes`() {
        // "中文" 的 GBK 编码：D6 D0 CE C4 —— 不是合法 UTF-8 序列
        val bytes = byteArrayOf(0xD6.toByte(), 0xD0.toByte(), 0xCE.toByte(), 0xC4.toByte())
        val info = DocumentCodings.detect(bytes)
        assertEquals("GBK", info.name)
    }

    @Test
    fun `encodes round trip for gbk`() {
        val text = "中文内容"
        val bytes = DocumentCodings.encode(text, "GBK")
        val info = DocumentCodings.detect(bytes)
        assertEquals("GBK", info.name)
        assertEquals(text, DocumentCodings.decode(bytes, info))
    }

    @Test
    fun `encode utf-8 has no bom by default`() {
        val bytes = DocumentCodings.encode("text", "UTF-8")
        assertTrue(bytes[0] != 0xEF.toByte())
    }

    @Test
    fun `unknown encoding falls back to utf-8`() {
        val bytes = DocumentCodings.encode("text", "NOT-A-THING")
        assertEquals("text", String(bytes, Charsets.UTF_8))
    }
}

/** DocumentRef stable string 往返契约（最近文件/会话恢复的序列化形式）。 */
class DocumentRefStableStringTest {

    @Test
    fun `saf ref round trips through stable string`() {
        val ref = DocumentRef.Saf("content://com.android.externalstorage.documents/tree/primary%3ADocs/document/primary%3ADocs%2Fnote.md")
        val restored = documentRefFromStableString(documentRefToStableString(ref))
        assertEquals(ref, restored)
    }

    @Test
    fun `invalid stable string returns null`() {
        assertNull(documentRefFromStableString("not-a-ref"))
        assertNull(documentRefFromStableString(""))
    }
}
