package com.markleaf.app.ui

import android.content.SharedPreferences

/** 编辑器外观偏好（字段与默认值对齐桌面 AppSettings 同名项）。
 *  SharedPreferences 持久化；改值即生效（applyVisualVariables / ApplyStyles / 缩放）。 */
data class EditorPrefs(
    val fontSize: Int = 16,
    val lineHeight: Double = 1.75,
    val maxWidth: Int = 820,
    val ignoreMaxWidth: Boolean = false,
    val cjkSpacing: Boolean = true,
    val tableColumnResize: Boolean = true,
    val zoom: Int = 100,
    val styleId: String = "serif",
    val colorTheme: String = "colors-default-light",
    val sourceFontSize: Int = 14,
    val adaptiveJustification: Boolean = true,
    val defaultEncoding: String = "UTF-8",
    val defaultNewline: String = "LF",
) {
    fun save(sp: SharedPreferences) {
        sp.edit()
            .putInt(PREF_FONT_SIZE, fontSize)
            .putFloat(PREF_LINE_HEIGHT, lineHeight.toFloat())
            .putInt(PREF_MAX_WIDTH, maxWidth)
            .putBoolean(PREF_IGNORE_MAX_WIDTH, ignoreMaxWidth)
            .putBoolean(PREF_CJK_SPACING, cjkSpacing)
            .putBoolean(PREF_TABLE_COLUMN_RESIZE, tableColumnResize)
            .putInt(PREF_ZOOM, zoom)
            .putString(PREF_STYLE_ID, styleId)
            .putString(PREF_COLOR_THEME, colorTheme)
            .putInt(PREF_SOURCE_FONT_SIZE, sourceFontSize)
            .putBoolean(PREF_ADAPTIVE_JUSTIFICATION, adaptiveJustification)
            .putString(PREF_DEFAULT_ENCODING, defaultEncoding)
            .putString(PREF_DEFAULT_NEWLINE, defaultNewline)
            .apply()
    }

    companion object {
        private const val PREF_FONT_SIZE = "fontSize"
        private const val PREF_LINE_HEIGHT = "lineHeight"
        private const val PREF_MAX_WIDTH = "maxWidth"
        private const val PREF_IGNORE_MAX_WIDTH = "ignoreMaxWidth"
        private const val PREF_CJK_SPACING = "cjkSpacing"
        private const val PREF_TABLE_COLUMN_RESIZE = "tableColumnResize"
        private const val PREF_ZOOM = "zoom"
        private const val PREF_STYLE_ID = "styleId"
        private const val PREF_COLOR_THEME = "colorTheme"
        private const val PREF_SOURCE_FONT_SIZE = "sourceFontSize"
        private const val PREF_ADAPTIVE_JUSTIFICATION = "adaptiveJustification"
        private const val PREF_DEFAULT_ENCODING = "defaultEncoding"
        private const val PREF_DEFAULT_NEWLINE = "defaultNewline"

        /** 可选编码（与桌面 DocumentEncodingPolicy 同名同序）。 */
        val ENCODING_CHOICES = listOf(
            "UTF-8",
            "UTF-8 with BOM",
            "UTF-16",
            "UTF-16 with BOM",
            "GB18030",
            "GBK",
            "GB2312",
            "Big5",
            "Shift_JIS",
            "US-ASCII",
        )

        /** 可选换行符（对齐桌面 newLinePopup）。 */
        val NEWLINE_CHOICES = listOf("LF", "CRLF")

        fun load(sp: SharedPreferences): EditorPrefs = EditorPrefs(
            fontSize = sp.getInt(PREF_FONT_SIZE, 16).coerceIn(12, 24),
            lineHeight = sp.getFloat(PREF_LINE_HEIGHT, 1.75f).toDouble(),
            maxWidth = sp.getInt(PREF_MAX_WIDTH, 820),
            ignoreMaxWidth = sp.getBoolean(PREF_IGNORE_MAX_WIDTH, false),
            cjkSpacing = sp.getBoolean(PREF_CJK_SPACING, true),
            tableColumnResize = sp.getBoolean(PREF_TABLE_COLUMN_RESIZE, true),
            zoom = sp.getInt(PREF_ZOOM, 100).coerceIn(50, 200),
            styleId = sp.getString(PREF_STYLE_ID, "serif") ?: "serif",
            colorTheme = sp.getString(PREF_COLOR_THEME, "colors-default-light") ?: "colors-default-light",
            sourceFontSize = sp.getInt(PREF_SOURCE_FONT_SIZE, 14).coerceIn(12, 20),
            adaptiveJustification = sp.getBoolean(PREF_ADAPTIVE_JUSTIFICATION, true),
            defaultEncoding = sp.getString(PREF_DEFAULT_ENCODING, "UTF-8") ?: "UTF-8",
            defaultNewline = sp.getString(PREF_DEFAULT_NEWLINE, "LF") ?: "LF",
        )
    }
}
