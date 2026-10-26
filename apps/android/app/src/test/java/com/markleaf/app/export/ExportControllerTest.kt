package com.markleaf.app.export

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** 导出控制器纯逻辑契约（spec §5）。 */
class ExportControllerTest {

    // ---- 文件名清洗（与 Windows saveName 同规则）----

    @Test
    fun `strips illegal filename characters`() {
        assertEquals("报告最终", ExportController.sanitizeFileName("报告:最终"))
        assertEquals("abc", ExportController.sanitizeFileName("a\\b/c"))
        // 星号不在 Windows 非法集？在 —— GetInvalidFileNameChars 含 *；期望剔除
        assertEquals("问题", ExportController.sanitizeFileName("问题*"))
    }

    @Test
    fun `strips quotes and truncates to 64`() {
        assertEquals("我的文件", ExportController.sanitizeFileName("我的\"文?件"))
        assertEquals(64, ExportController.sanitizeFileName("长".repeat(80)).length)
    }

    @Test
    fun `empty result falls back to document`() {
        assertEquals("document", ExportController.sanitizeFileName("???"))
        assertEquals("document", ExportController.sanitizeFileName(""))
    }

    @Test
    fun `trims trailing dots and spaces`() {
        assertEquals("note", ExportController.sanitizeFileName("note. "))
    }

    // ---- 导出选项 ----

    @Test
    fun `html options carry no paper settings`() {
        val options = ExportOptions(format = ExportFormat.HTML)
        assertTrue(options.paperSize == null)
        assertTrue(options.printFriendly is Boolean)
    }

    @Test
    fun `pdf options default a4 portrait`() {
        val options = ExportOptions(format = ExportFormat.PDF)
        assertEquals("A4", options.paperSize)
        assertEquals(false, options.landscape)
    }

    // ---- 打印属性参数 ----

    @Test
    fun `job name uses sanitized document name`() {
        assertEquals("MarkLeaf 报告", ExportController.jobName("报告"))
        assertTrue(ExportController.jobName("a/b").endsWith("b"))
    }
}
