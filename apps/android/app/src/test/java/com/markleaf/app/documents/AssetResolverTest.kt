package com.markleaf.app.documents

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** AssetResolver 路径解析契约：相对/绝对/越界拒绝（Review Focus：目录逃逸）。 */
class AssetResolverTest {

    // 键 = 相对授权根的 POSIX 归一化路径（loader 收到的就是它）
    private val images = mapOf(
        "Docs/note-assets/pic.png" to byteArrayOf(1, 2, 3),
        "Docs/note-assets/sub/deep.jpg" to byteArrayOf(4, 5),
        "Docs/cover.png" to byteArrayOf(9),
    )

    private fun resolver(documentDir: String = "primary:Docs/note-assets") =
        AssetResolver { path -> images[path]?.copyOf() }

    @Test
    fun `resolves relative path against document directory`() {
        val resolver = resolver()
        val bytes = resolver.resolve("note-assets/pic.png", documentDir = "Docs")
        assertArrayEquals(byteArrayOf(1, 2, 3), bytes)
    }

    @Test
    fun `resolves sibling absolute path`() {
        val resolver = resolver()
        assertArrayEquals(byteArrayOf(9), resolver.resolve("cover.png", documentDir = "Docs"))
    }

    @Test
    fun `normalizes dot-dot without escaping`() {
        val resolver = resolver()
        // ../ 回到 Docs 再进 note-assets —— 合法
        assertArrayEquals(
            byteArrayOf(4, 5),
            resolver.resolve("note-assets/../note-assets/sub/deep.jpg", documentDir = "Docs"),
        )
    }

    @Test
    fun `rejects path escaping the workspace`() {
        val resolver = resolver()
        // 归一化后落在授权根之外 → null（不读文件）
        assertNull(resolver.resolve("../../system/etc/hosts", documentDir = "Docs"))
        assertNull(resolver.resolve("/data/misc/keys", documentDir = "Docs"))
    }

    @Test
    fun `rejects url-encoded traversal`() {
        val resolver = resolver()
        assertNull(resolver.resolve("note-assets/%2e%2e/%2e%2e/secret", documentDir = "Docs"))
    }

    @Test
    fun `returns null for missing image`() {
        val resolver = resolver()
        assertNull(resolver.resolve("note-assets/missing.png", documentDir = "Docs"))
    }

    @Test
    fun `join produces normalized posix path`() {
        assertEquals("Docs/note-assets/pic.png", AssetResolver.join("Docs", "note-assets/pic.png"))
        assertEquals("Docs/pic.png", AssetResolver.join("Docs", "../Docs/pic.png"))
    }
}
