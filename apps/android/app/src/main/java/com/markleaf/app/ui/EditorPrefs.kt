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
    val colorTheme: String = DEFAULT_LIGHT_THEME,
    val sourceFontSize: Int = 14,
    val sourceFontFamily: String = "monospace",
    val cjkLanguage: String = "zh-Hans",
    val adaptiveJustification: Boolean = true,
    val codeHighlight: Boolean = true,
    val blockHandle: Boolean = true,
    val sourceIndent: Int = 2,
    val codeFence: String = "backtick",
    val emphasisMarker: String = "asterisk",
    val bulletMarker: String = "dash",
    val exitBlockOnEmptyEnter: Boolean = false,
    val useShiftEnterHardBreak: Boolean = true,
    val snapshotInterval: Int = 30,
    val displayLanguage: String = detectDisplayLanguage(),
    val recordRecent: Boolean = true,
    val autoSave: Boolean = false,
    val saveOnSwitch: Boolean = true,
    val defaultEncoding: String = "UTF-8",
    val defaultNewline: String = "LF",
    // 桌面 Markdown 行为子窗口其余三项
    val escapeLiteralSymbols: Boolean = false,
    val escapeMarkdownLiteralSymbols: Boolean = false,
    val codeBlockSpellcheck: Boolean = false,
    // 桌面外观页「文档外观」：与操作系统同步 + 默认浅/深色主题
    val followSystemTheme: Boolean = true,
    val defaultLightTheme: String = DEFAULT_LIGHT_THEME,
    val defaultDarkTheme: String = DEFAULT_DARK_THEME,
    // 桌面编辑器页「缩放视图」；pinchZoom 对应桌面「⌘ + 滚轮缩放」的触屏等价项
    val restoreZoomOnOpen: Boolean = true,
    val pinchZoom: Boolean = true,
    // 桌面图片页
    val prefixRelativeWithDotSlash: Boolean = true,
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
            .putBoolean(PREF_BLOCK_HANDLE, blockHandle)
            .putInt(PREF_SOURCE_INDENT, sourceIndent)
            .putString(PREF_CODE_FENCE, codeFence)
            .putString(PREF_EMPHASIS_MARKER, emphasisMarker)
            .putString(PREF_BULLET_MARKER, bulletMarker)
            .putBoolean(PREF_EXIT_BLOCK_ON_EMPTY_ENTER, exitBlockOnEmptyEnter)
            .putBoolean(PREF_USE_SHIFT_ENTER_HARD_BREAK, useShiftEnterHardBreak)
            .putInt(PREF_SNAPSHOT_INTERVAL, snapshotInterval)
            .putString(PREF_DISPLAY_LANGUAGE, displayLanguage)
            .putBoolean(PREF_RECORD_RECENT, recordRecent)
            .putBoolean(PREF_AUTO_SAVE, autoSave)
            .putBoolean(PREF_SAVE_ON_SWITCH, saveOnSwitch)
            .putString(PREF_DEFAULT_ENCODING, defaultEncoding)
            .putString(PREF_DEFAULT_NEWLINE, defaultNewline)
            .putBoolean(PREF_ESCAPE_LITERAL_SYMBOLS, escapeLiteralSymbols)
            .putBoolean(PREF_ESCAPE_MARKDOWN_LITERAL_SYMBOLS, escapeMarkdownLiteralSymbols)
            .putBoolean(PREF_CODE_BLOCK_SPELLCHECK, codeBlockSpellcheck)
            .putBoolean(PREF_FOLLOW_SYSTEM_THEME, followSystemTheme)
            .putString(PREF_DEFAULT_LIGHT_THEME, defaultLightTheme)
            .putString(PREF_DEFAULT_DARK_THEME, defaultDarkTheme)
            .putBoolean(PREF_RESTORE_ZOOM_ON_OPEN, restoreZoomOnOpen)
            .putBoolean(PREF_PINCH_ZOOM, pinchZoom)
            .putBoolean(PREF_PREFIX_RELATIVE_WITH_DOT_SLASH, prefixRelativeWithDotSlash)
            .apply()
    }

    companion object {
        /** 默认浅/深色主题（对齐桌面 AppSettings：apple-blue / apple-dark）。 */
        const val DEFAULT_LIGHT_THEME = "colors-apple-blue"
        const val DEFAULT_DARK_THEME = "colors-apple-dark"

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
        private const val PREF_BLOCK_HANDLE = "blockHandle"
        private const val PREF_SOURCE_INDENT = "sourceIndent"
        private const val PREF_CODE_FENCE = "codeFence"
        private const val PREF_EMPHASIS_MARKER = "emphasisMarker"
        private const val PREF_BULLET_MARKER = "bulletMarker"
        private const val PREF_EXIT_BLOCK_ON_EMPTY_ENTER = "exitBlockOnEmptyEnter"
        private const val PREF_USE_SHIFT_ENTER_HARD_BREAK = "useShiftEnterHardBreak"
        private const val PREF_SNAPSHOT_INTERVAL = "snapshotInterval"
        private const val PREF_DISPLAY_LANGUAGE = "editorLanguage"
        private const val PREF_RECORD_RECENT = "recordRecent"
        private const val PREF_AUTO_SAVE = "autoSave"
        private const val PREF_SAVE_ON_SWITCH = "saveOnSwitch"
        private const val PREF_DEFAULT_ENCODING = "defaultEncoding"
        private const val PREF_DEFAULT_NEWLINE = "defaultNewline"
        private const val PREF_ESCAPE_LITERAL_SYMBOLS = "escapeLiteralSymbols"
        private const val PREF_ESCAPE_MARKDOWN_LITERAL_SYMBOLS = "escapeMarkdownLiteralSymbols"
        private const val PREF_CODE_BLOCK_SPELLCHECK = "codeBlockSpellcheck"
        private const val PREF_FOLLOW_SYSTEM_THEME = "followSystemTheme"
        private const val PREF_DEFAULT_LIGHT_THEME = "defaultLightTheme"
        private const val PREF_DEFAULT_DARK_THEME = "defaultDarkTheme"
        private const val PREF_RESTORE_ZOOM_ON_OPEN = "restoreZoomOnOpen"
        private const val PREF_PINCH_ZOOM = "pinchZoom"
        private const val PREF_PREFIX_RELATIVE_WITH_DOT_SLASH = "prefixRelativeWithDotSlash"

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

        /** 代码围栏（对齐桌面 markdownCodeFence；展示名为桌面 L10n 键）。 */
        val CODE_FENCES = listOf("backtick" to "反引号 `", "tilde" to "波浪号 ~")

        /** 强调标记（对齐桌面 markdownEmphasisMarker）。 */
        val EMPHASIS_MARKERS = listOf("asterisk" to "星号 *", "underscore" to "下划线 _")

        /** 无序列表符号（对齐桌面 markdownBulletMarker）。 */
        val BULLET_MARKERS = listOf("dash" to "短横线 -", "asterisk" to "星号 *", "plus" to "加号 +")

        /** 显示语言（对齐桌面 displayLanguage；驱动宿主 L10n 与编辑器内核）。 */
        val DISPLAY_LANGUAGES = listOf(
            "zh-Hans" to "简体中文",
            "zh-Hant" to "繁體中文",
            "en" to "English",
            "ja" to "日本語",
        )

        /** 系统语言探测（对齐桌面 detectSystemLanguage）。 */
        fun detectDisplayLanguage(): String {
            val tag = java.util.Locale.getDefault().toLanguageTag()
            return when {
                tag.startsWith("zh-TW") || tag.startsWith("zh-HK") || tag.startsWith("zh-MO") ||
                    tag.startsWith("zh-Hant") -> "zh-Hant"
                tag.startsWith("ja") -> "ja"
                tag.startsWith("en") -> "en"
                else -> "zh-Hans"
            }
        }

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
            colorTheme = sp.getString(PREF_COLOR_THEME, DEFAULT_LIGHT_THEME) ?: DEFAULT_LIGHT_THEME,
            sourceFontSize = sp.getInt(PREF_SOURCE_FONT_SIZE, 14).coerceIn(12, 20),
            sourceFontFamily = sp.getString(PREF_SOURCE_FONT_FAMILY, "monospace") ?: "monospace",
            cjkLanguage = sp.getString(PREF_CJK_LANGUAGE, "zh-Hans") ?: "zh-Hans",
            adaptiveJustification = sp.getBoolean(PREF_ADAPTIVE_JUSTIFICATION, true),
            codeHighlight = sp.getBoolean(PREF_CODE_HIGHLIGHT, true),
            blockHandle = sp.getBoolean(PREF_BLOCK_HANDLE, true),
            sourceIndent = sp.getInt(PREF_SOURCE_INDENT, 2).coerceIn(1, 8),
            codeFence = sp.getString(PREF_CODE_FENCE, "backtick") ?: "backtick",
            emphasisMarker = sp.getString(PREF_EMPHASIS_MARKER, "asterisk") ?: "asterisk",
            bulletMarker = sp.getString(PREF_BULLET_MARKER, "dash") ?: "dash",
            exitBlockOnEmptyEnter = sp.getBoolean(PREF_EXIT_BLOCK_ON_EMPTY_ENTER, false),
            useShiftEnterHardBreak = sp.getBoolean(PREF_USE_SHIFT_ENTER_HARD_BREAK, true),
            snapshotInterval = sp.getInt(PREF_SNAPSHOT_INTERVAL, 30).coerceIn(10, 300),
            displayLanguage = sp.getString(PREF_DISPLAY_LANGUAGE, null) ?: detectDisplayLanguage(),
            recordRecent = sp.getBoolean(PREF_RECORD_RECENT, true),
            autoSave = sp.getBoolean(PREF_AUTO_SAVE, false),
            saveOnSwitch = sp.getBoolean(PREF_SAVE_ON_SWITCH, true),
            defaultEncoding = sp.getString(PREF_DEFAULT_ENCODING, "UTF-8") ?: "UTF-8",
            defaultNewline = sp.getString(PREF_DEFAULT_NEWLINE, "LF") ?: "LF",
            escapeLiteralSymbols = sp.getBoolean(PREF_ESCAPE_LITERAL_SYMBOLS, false),
            escapeMarkdownLiteralSymbols = sp.getBoolean(PREF_ESCAPE_MARKDOWN_LITERAL_SYMBOLS, false),
            codeBlockSpellcheck = sp.getBoolean(PREF_CODE_BLOCK_SPELLCHECK, false),
            followSystemTheme = sp.getBoolean(PREF_FOLLOW_SYSTEM_THEME, true),
            defaultLightTheme = sp.getString(PREF_DEFAULT_LIGHT_THEME, DEFAULT_LIGHT_THEME) ?: DEFAULT_LIGHT_THEME,
            defaultDarkTheme = sp.getString(PREF_DEFAULT_DARK_THEME, DEFAULT_DARK_THEME) ?: DEFAULT_DARK_THEME,
            restoreZoomOnOpen = sp.getBoolean(PREF_RESTORE_ZOOM_ON_OPEN, true),
            pinchZoom = sp.getBoolean(PREF_PINCH_ZOOM, true),
            prefixRelativeWithDotSlash = sp.getBoolean(PREF_PREFIX_RELATIVE_WITH_DOT_SLASH, true),
        )
    }
}
