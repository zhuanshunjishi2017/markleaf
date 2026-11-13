package com.markleaf.app.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class ThemeSelectionPolicyTest {
    @Test
    fun disablingSystemSyncKeepsTheRenderedTheme() {
        val prefs = EditorPrefs(followSystemTheme = false, colorTheme = "colors-old")
        val normalized = ThemeSelectionPolicy.preserveRenderedThemeWhenDisablingSystemSync(
            oldFollowSystem = true,
            newPrefs = prefs,
            renderedThemeId = "colors-apple-blue",
        )

        assertEquals("colors-apple-blue", normalized.colorTheme)
    }

    @Test
    fun unrelatedPreferenceChangesDoNotRewriteManualTheme() {
        val prefs = EditorPrefs(followSystemTheme = false, colorTheme = "colors-manual")
        val normalized = ThemeSelectionPolicy.preserveRenderedThemeWhenDisablingSystemSync(
            oldFollowSystem = false,
            newPrefs = prefs,
            renderedThemeId = "colors-apple-blue",
        )

        assertEquals("colors-manual", normalized.colorTheme)
    }
}
