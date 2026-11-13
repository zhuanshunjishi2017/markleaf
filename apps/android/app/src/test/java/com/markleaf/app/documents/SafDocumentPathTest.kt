package com.markleaf.app.documents

import org.junit.Assert.assertEquals
import org.junit.Test

class SafDocumentPathTest {

    @Test
    fun `parent of nested external storage document keeps directory`() {
        assertEquals("primary:Docs/assets", SafDocumentPath.parentDocumentId("primary:Docs/assets/note.md"))
    }

    @Test
    fun `parent of root external storage document keeps volume root`() {
        assertEquals("primary:", SafDocumentPath.parentDocumentId("primary:note.md"))
    }

    @Test
    fun `segments preserve nested relative image path`() {
        assertEquals(
            listOf("assets", "images", "cover.png"),
            SafDocumentPath.segments("/assets/images/cover.png"),
        )
    }

    @Test
    fun `segments drop parent references`() {
        assertEquals(listOf("assets", "cover.png"), SafDocumentPath.segments("../assets/./cover.png"))
        assertEquals(emptyList<String>(), SafDocumentPath.segments(".."))
    }

    @Test
    fun `walk resolves nested segments starting at the given root`() {
        // 回归护栏：曾从传入根的父目录起步，导致嵌套图片全 404。
        // 树里只有 Docs 子树；若实现先取父（落到卷根 primary:），首段即 miss。
        val tree = mapOf(
            "primary:Docs" to listOf("assets"),
            "primary:Docs/assets" to listOf("cover.png"),
        )
        val result = SafDocumentPath.walkDocumentId("primary:Docs", listOf("assets", "cover.png")) { parent, name ->
            if (tree[parent]?.contains(name) == true) "$parent/$name" else null
        }
        assertEquals("primary:Docs/assets/cover.png", result)
    }

    @Test
    fun `walk returns null when any segment misses`() {
        val tree = mapOf("primary:Docs" to listOf("assets"))
        val result = SafDocumentPath.walkDocumentId("primary:Docs", listOf("assets", "missing.png")) { parent, name ->
            if (tree[parent]?.contains(name) == true) "$parent/$name" else null
        }
        assertEquals(null, result)
    }

}
