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
    val sourceFontFamily: String = "monospace",
    val cjkLanguage: String = "zh-Hans",
    val adaptiveJustification: Boolean = true,
    val codeHighlight: Boolean = true,
    val autoSave: Boolean = false,
    val saveOnSwitch: Boolean = true,
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
            .putString(PREF_SOURCE_FONT_FAMILY, sourceFontFamily)
            .putString(PREF_CJK_LANGUAGE, cjkLanguage)
            .putBoolean(PREF_ADAPTIVE_JUSTIFICATION, adaptiveJustification)
            .putBoolean(PREF_CODE_HIGHLIGHT, codeHighlight)
            .putBoolean(PREF_AUTO_SAVE, autoSave)
            .putBoolean(PREF_SAVE_ON_SWITCH, saveOnSwitch)
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
        private const val PREF_SOURCE_FONT_FAMILY = "sourceFontFamily"
        private const val PREF_CJK_LANGUAGE = "cjkLanguage"
        private const val PREF_ADAPTIVE_JUSTIFICATION = "adaptiveJustification"
        private const val PREF_CODE_HIGHLIGHT = "codeHighlight"
        private const val PREF_AUTO_SAVE = "autoSave"
        private const val PREF_SAVE_ON_SWITCH = "saveOnSwitch"
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

        /** 源码字体（Android 通用字族；桌面为具体字体名）。 */
        val SOURCE_FONT_FAMILIES = listOf(
            "monospace" to "等宽",
            "serif" to "衬线",
            "sans-serif" to "无衬线",
        )

        /** 中文排版语言（对齐桌面 CJKLanguageTag）。 */
        val CJK_LANGUAGES = listOf(
            "zh-Hans" to "简体中文",
            "zh-Hant" to "繁體中文",
            "ja" to "日本語",
            "ko" to "한국어",
        )

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
            sourceFontFamily = sp.getString(PREF_SOURCE_FONT_FAMILY, "monospace") ?: "monospace",
            cjkLanguage = sp.getString(PREF_CJK_LANGUAGE, "zh-Hans") ?: "zh-Hans",
            adaptiveJustification = sp.getBoolean(PREF_ADAPTIVE_JUSTIFICATION, true),
            codeHighlight = sp.getBoolean(PREF_CODE_HIGHLIGHT, true),
            autoSave = sp.getBoolean(PREF_AUTO_SAVE, false),
            saveOnSwitch = sp.getBoolean(PREF_SAVE_ON_SWITCH, true),
            defaultEncoding = sp.getString(PREF_DEFAULT_ENCODING, "UTF-8") ?: "UTF-8",
            defaultNewline = sp.getString(PREF_DEFAULT_NEWLINE, "LF") ?: "LF",
        )
    }
}
