package com.markleaf.app.editor

import org.junit.Assert.assertEquals
import org.junit.Test

class StyleAssetsTest {
    @Test
    fun defaultLightThemeFollowsSystemDarkMode() {
        assertEquals(
            StyleAssets.DEFAULT_DARK_THEME,
            StyleAssets.resolveColorThemeId(StyleAssets.DEFAULT_COLOR_THEME, systemDark = true),
        )
    }

    @Test
    fun explicitThemeIsPreservedAcrossSystemModeChanges() {
        assertEquals(
            "colors-morandi-cyan",
            StyleAssets.resolveColorThemeId("colors-morandi-cyan", systemDark = true),
        )
        assertEquals(
            "colors-apple-dark",
            StyleAssets.resolveColorThemeId("colors-apple-dark", systemDark = false),
        )
    }
}
