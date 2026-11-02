package com.markleaf.app.editor

import android.content.Context

/**
 * 文档主题样式资产（spec §4/§7）：applyStyles 载荷由 base.css（排版基础，
 * 定义 --ml-* 变量与文档排版）+ 颜色主题 css + 排版样式 css 组装。
 * 颜色主题目录运行时从 assets 解析（对齐桌面 StyleManager：id/@name/@mode
 * 取自 css 头部元数据注释，@mode 决定浅色/深色归类），排版样式目录为静态清单。
 */
object StyleAssets {

    const val BASE_CSS = "editor/styles/base.css"
    const val DEFAULT_TYPOGRAPHY_ID = "serif"

    /** 内置兜底主题（目录缺项/首选项存了已下线主题时使用；对齐桌面 StyleManager）。 */
    const val FALLBACK_LIGHT_THEME = "colors-default-light"
    const val FALLBACK_DARK_THEME = "colors-dark"

    /** 颜色主题（@name 为 css 元数据原文名，展示时经 L10n；isDark 来自 @mode）。 */
    data class ColorTheme(val id: String, val name: String, val isDark: Boolean)

    /** 排版样式（id 对齐 styles 目录下同名排版 css 文件名）。 */
    val TYPOGRAPHY_STYLES: List<Pair<String, String>> = listOf(
        "serif" to "衬线（默认）",
        "sans" to "无衬线",
        "minimal" to "极简",
        "notebook" to "笔记本",
        "magazine" to "杂志",
    )

    fun readBaseCss(context: Context): String = read(context, BASE_CSS)

    fun readColorThemeCss(context: Context, themeId: String): String =
        read(context, "editor/styles/$themeId.css")

    fun readTypographyCss(context: Context, styleId: String): String =
        read(context, "editor/styles/$styleId.css")

    /** 颜色主题元数据解析：取 css 头部注释块里的 `@name:` 与 `@mode:`。 */
    fun parseThemeMeta(css: String): Pair<String, Boolean> {
        val header = css.substringBefore("*/")
        val name = Regex("@name:\\s*(.+)").find(header)?.groupValues?.get(1)?.trim().orEmpty()
        val isDark = Regex("@mode:\\s*(\\w+)").find(header)?.groupValues?.get(1) == "dark"
        return name to isDark
    }

    @Volatile
    private var cachedThemes: List<ColorTheme>? = null

    /** 颜色主题目录（按文件名排序，缓存一次；与桌面 StyleManager 同构）。 */
    fun colorThemes(context: Context): List<ColorTheme> {
        cachedThemes?.let { return it }
        val themes = context.assets.list("editor/styles").orEmpty()
            .filter { it.startsWith("colors-") && it.endsWith(".css") }
            .sorted()
            .map { fileName ->
                val id = fileName.removeSuffix(".css")
                val (name, isDark) = parseThemeMeta(read(context, "editor/styles/$fileName"))
                ColorTheme(id, name.ifEmpty { id }, isDark)
            }
        cachedThemes = themes
        return themes
    }

    /**
     * 解析生效主题：关闭「与操作系统同步」且已选主题在目录内 → 已选；
     * 否则按系统外观取默认浅/深主题，缺失时逐级回退到内置兜底
     * （对齐桌面 StyleManager.defaultThemeID 的回退链）。
     */
    fun resolveColorThemeId(
        selected: String,
        followSystem: Boolean,
        systemDark: Boolean,
        preferredLight: String,
        preferredDark: String,
        catalog: List<ColorTheme>,
    ): String {
        fun contains(id: String) = catalog.any { it.id == id }
        if (!followSystem && contains(selected)) return selected
        val preferred = if (systemDark) preferredDark else preferredLight
        if (contains(preferred)) return preferred
        val builtIn = if (systemDark) FALLBACK_DARK_THEME else FALLBACK_LIGHT_THEME
        if (contains(builtIn)) return builtIn
        return if (systemDark) catalog.firstOrNull { it.isDark }?.id ?: catalog.firstOrNull()?.id ?: builtIn
        else catalog.firstOrNull()?.id ?: builtIn
    }

    private fun read(context: Context, path: String): String =
        context.assets.open(path).use { stream ->
            stream.readBytes().decodeToString()
        }
}
