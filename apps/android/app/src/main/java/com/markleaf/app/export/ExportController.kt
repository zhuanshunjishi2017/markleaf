package com.markleaf.app.export

/**
 * 导出格式。HTML 走前端 export-html 分包产自包含 HTML；
 * PDF 走 WebView createPrintDocumentAdapter + PrintManager（spec §5.2）。
 */
enum class ExportFormat { HTML, PDF }

/**
 * 导出选项（对应桌面 ExportOptions 的移动端子集）。
 * PDF 页眉/页脚：第一版由系统打印框架决定，自定义页眉页脚**显式降级**（spec §5.2）。
 */
data class ExportOptions(
    val format: ExportFormat,
    val paperSize: String? = if (format == ExportFormat.PDF) "A4" else null,
    val landscape: Boolean = false,
    val printFriendly: Boolean = false,
)

/** 导出控制器：文件名清洗、job 名、导出编排的纯逻辑部分。 */
object ExportController {

    /**
     * Windows ImageAssetService.SanitizeFileName 同规则：
     * 剔除非法字符（\/:*?"<>| 与控制符）、限长 64、空白回退 "document"。
     */
    fun sanitizeFileName(raw: String): String {
        if (raw.isBlank()) {
            return "document"
        }
        val invalid = charArrayOf('\\', '/', ':', '*', '?', '"', '<', '>', '|')
        val cleaned = raw
            .filter { character -> invalid.none { it == character } && character >= ' ' }
            .take(64)
            .trim()
            .trimEnd('.', ' ')
        return cleaned.ifEmpty { "document" }
    }

    /** 打印任务名：应用前缀 + 清洗后的文档名。 */
    fun jobName(documentName: String): String = "MarkLeaf ${sanitizeFileName(documentName)}"
}
