package com.markleaf.app.editor

import android.content.Context

/**
 * 文档主题样式资产（spec §4/§7）：applyStyles 载荷需要 base.css（排版基础，
 * 定义 --ml-* 变量与文档排版）、默认浅色主题（背景/文字等主题变量）、
 * serif 排版样式。缺失会导致表格无边框、代码块无背景、公式无编辑框。
 */
object StyleAssets {

    const val BASE_CSS = "editor/styles/base.css"
    const val COLOR_THEME_CSS = "editor/styles/colors-default-light.css"
    const val TYPOGRAPHY_CSS = "editor/styles/serif.css"
    const val DEFAULT_TYPOGRAPHY_ID = "serif"

    fun readBaseCss(context: Context): String = read(context, BASE_CSS)

    fun readColorThemeCss(context: Context): String = read(context, COLOR_THEME_CSS)

    fun readTypographyCss(context: Context): String = read(context, TYPOGRAPHY_CSS)

    private fun read(context: Context, path: String): String =
        context.assets.open(path).use { stream ->
            stream.readBytes().decodeToString()
        }
}
