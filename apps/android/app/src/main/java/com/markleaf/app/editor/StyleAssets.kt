package com.markleaf.app.editor

import android.content.Context

/**
 * 文档主题样式资产（spec §4/§7）：applyStyles 载荷由 base.css（排版基础，
 * 定义 --ml-* 变量与文档排版）+ 颜色主题 css + 排版样式 css 组装。
 * 颜色主题与排版样式目录供首选项页选择（对齐 packages/styles 清单）。
 */
object StyleAssets {

    const val BASE_CSS = "editor/styles/base.css"
    const val DEFAULT_COLOR_THEME = "colors-default-light"
    const val DEFAULT_TYPOGRAPHY_ID = "serif"

    /** 颜色主题（id 对齐 styles 目录下 colors 开头的主题 css 文件名）。 */
    val COLOR_THEMES: List<Pair<String, String>> = listOf(
        "colors-default-light" to "浅色（默认）",
        "colors-apple-blue" to "Apple Blue",
        "colors-apple-dark" to "Apple Dark",
        "colors-dark" to "深色",
        "colors-deep-sea" to "深海",
        "colors-espresso" to "浓缩咖啡",
        "colors-forest" to "森林",
        "colors-high-contrast-dark" to "高对比深色",
        "colors-high-contrast-light" to "高对比浅色",
        "colors-ink" to "墨色",
        "colors-lavender" to "薰衣草",
        "colors-memo" to "备忘",
        "colors-morandi" to "莫兰迪",
        "colors-morandi-cyan" to "莫兰迪青",
        "colors-morandi-dark" to "莫兰迪深",
        "colors-pure-black" to "纯黑",
        "colors-rose" to "玫瑰",
        "colors-saltlemon" to "盐柠",
        "colors-yellowed-page" to "泛黄书页",
    )

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

    private fun read(context: Context, path: String): String =
        context.assets.open(path).use { stream ->
            stream.readBytes().decodeToString()
        }
}
