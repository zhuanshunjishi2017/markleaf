package com.markleaf.app.export

import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject

/**
 * 导出格式。HTML 走前端 export-html 分包产自包含 HTML；
 * PDF 走专用 WebView 的打印适配器，预览与保存复用同一分页文件。
 */
enum class ExportFormat { HTML, PDF, IMAGE }

/**
 * 导出选项（对应桌面 ExportOptions 的移动端子集）。
 * PDF 页眉/页脚由导出 HTML 统一生成，预览与保存复用同一份分页文件。
 */
data class ExportOptions(
    val format: ExportFormat,
    val paperSize: String? = if (format == ExportFormat.PDF) "A4" else null,
    val landscape: Boolean = false,
    val printFriendly: Boolean = false,
    val marginPresetId: String = "standard",
    // 自定义边距（mm，对应桌面 ExportMargins；marginPresetId == "custom" 时生效）
    val marginTop: Double = 18.0,
    val marginBottom: Double = 18.0,
    val marginLeft: Double = 15.0,
    val marginRight: Double = 15.0,
    val keepTablesTogether: Boolean = true,
    val keepHeadingsWithNextBlock: Boolean = true,
    val headerPresetId: String = "none",
    val footerPresetId: String = "none",
    val headerCustom: String = "",
    val footerCustom: String = "",
    val imageFormat: String = "png",
    val imageJpegQuality: Int = 90,
    val imageScale: Int = 2,
    val styleId: String = "serif",
    val colorThemeId: String = "",
)

/** 导出控制器：文件名清洗、job 名、导出编排的纯逻辑部分。 */
object ExportController {

    /** The same content request is used for preview and the saved document. */
    fun htmlRequest(title: String, options: ExportOptions, colorSchemeCss: String = ""): String = buildJsonObject {
        put("format", JsonPrimitive(options.format.name.lowercase()))
        put("style", JsonPrimitive(options.styleId))
        put("colorSchemeCss", JsonPrimitive(colorSchemeCss))
        put("title", JsonPrimitive(title))
        put("keepTablesTogether", JsonPrimitive(options.keepTablesTogether))
        put("keepHeadingsWithNextBlock", JsonPrimitive(options.keepHeadingsWithNextBlock))
        // HTML 与 PDF 使用同一份导出文档。PDF 由固定页眉/页脚元素重复到
        // 每一页；页码占位符在 export-html.css 中由打印计数器渲染。
        val header = if (options.format == ExportFormat.IMAGE) "" else
            presetText(options.headerPresetId, title, options.headerCustom, options.format)
        val footer = if (options.format == ExportFormat.IMAGE) "" else
            presetText(options.footerPresetId, title, options.footerCustom, options.format)
        put("header", JsonPrimitive(header))
        put("footer", JsonPrimitive(footer))
    }.toString()

    private fun presetText(id: String, title: String, custom: String, format: ExportFormat): String {
        val (template, alignment) = when (id) {
            "title-left" -> title to "left"
            "page-center" -> "{page}" to "center"
            "page-right" -> "{page}" to "right"
            "page-total-center" -> "{page}/{pages}" to "center"
            "custom" -> custom to "center"
            else -> "" to "left"
        }
        if (template.isBlank()) return ""
        val escaped = escapeHtml(template)
            .replace("{page}", if (format == ExportFormat.PDF) "<span class=\"markleaf-page-number\"></span>" else "{page}")
            .replace("{pages}", if (format == ExportFormat.PDF) "<span class=\"markleaf-page-total\"></span>" else "{pages}")
        return if (format == ExportFormat.PDF) {
            "<span style=\"display:block;text-align:$alignment\">$escaped</span>"
        } else {
            escaped
        }
    }

    private fun escapeHtml(value: String): String = value
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace("\"", "&quot;")
        .replace("'", "&#39;")

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
