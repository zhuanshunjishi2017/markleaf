package com.markleaf.app.ui

/** 颜色主题选择状态与桌面端保持一致。 */
object ThemeSelectionPolicy {
    /** 从系统同步切换为手动时，保留当前已经渲染的主题。 */
    fun preserveRenderedThemeWhenDisablingSystemSync(
        oldFollowSystem: Boolean,
        newPrefs: EditorPrefs,
        renderedThemeId: String,
    ): EditorPrefs {
        return if (oldFollowSystem && !newPrefs.followSystemTheme && renderedThemeId.isNotBlank()) {
            newPrefs.copy(colorTheme = renderedThemeId)
        } else {
            newPrefs
        }
    }
}
