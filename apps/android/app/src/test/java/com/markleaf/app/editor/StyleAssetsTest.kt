package com.markleaf.app.editor

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 主题目录元数据解析 + 生效主题回退链（对齐桌面 StyleManager.defaultThemeID）。 */
class StyleAssetsTest {

    private val catalog = listOf(
        StyleAssets.ColorTheme("colors-apple-blue", "浅色", isDark = false),
        StyleAssets.ColorTheme("colors-apple-dark", "深色", isDark = true),
        StyleAssets.ColorTheme("colors-default-light", "旧版浅色", isDark = false),
        StyleAssets.ColorTheme("colors-dark", "旧版深色", isDark = true),
        StyleAssets.ColorTheme("colors-morandi-cyan", "莫兰迪青", isDark = false),
    )

    private fun resolve(
        selected: String = "colors-morandi-cyan",
        followSystem: Boolean,
        systemDark: Boolean,
        preferredLight: String = "colors-apple-blue",
        preferredDark: String = "colors-apple-dark",
        themes: List<StyleAssets.ColorTheme> = catalog,
    ) = StyleAssets.resolveColorThemeId(selected, followSystem, systemDark, preferredLight, preferredDark, themes)

    @Test
    fun parsesNameAndModeFromHeaderComment() {
        val css = "/* @type: color-theme\n   @name: 深色\n   @mode: dark */\n\n:root { --bg: #000; }"
        val (name, isDark) = StyleAssets.parseThemeMeta(css)
        assertEquals("深色", name)
        assertTrue(isDark)

        val light = StyleAssets.parseThemeMeta("/* @name: 浅色\n   @mode: light */ :root{}")
        assertEquals("浅色", light.first)
        assertFalse(light.second)
    }

    @Test
    fun parseIgnoresBodyAfterHeader() {
        // 正文里出现 @mode 不应影响判定（只读头部注释块）
        val css = "/* @name: X\n @mode: light */\n/* @mode: dark */ :root{}"
        assertFalse(StyleAssets.parseThemeMeta(css).second)
    }

    @Test
    fun followSystemPicksPreferredLightOrDarkTheme() {
        assertEquals("colors-apple-blue", resolve(followSystem = true, systemDark = false))
        assertEquals("colors-apple-dark", resolve(followSystem = true, systemDark = true))
    }

    @Test
    fun followSystemIgnoresExplicitSelection() {
        assertEquals(
            "colors-apple-dark",
            resolve(selected = "colors-morandi-cyan", followSystem = true, systemDark = true),
        )
    }

    @Test
    fun explicitThemeIsPreservedAcrossSystemModeChanges() {
        assertEquals("colors-morandi-cyan", resolve(followSystem = false, systemDark = true))
        assertEquals(
            "colors-apple-dark",
            resolve(selected = "colors-apple-dark", followSystem = false, systemDark = false),
        )
    }

    @Test
    fun missingPreferredFallsBackToBuiltInThenCatalog() {
        // 首选项存了已下线主题 → 内置兜底
        assertEquals(
            "colors-dark",
            resolve(followSystem = true, systemDark = true, preferredDark = "colors-removed"),
        )
        assertEquals(
            "colors-default-light",
            resolve(followSystem = true, systemDark = false, preferredLight = "colors-removed"),
        )
        // 关闭同步但已选主题不在目录 → 同样走默认浅/深链路
        assertEquals(
            "colors-apple-blue",
            resolve(selected = "colors-removed", followSystem = false, systemDark = false),
        )
        // 目录里连内置兜底都没有 → 深色取首个深色主题
        val tiny = listOf(
            StyleAssets.ColorTheme("colors-a", "A", isDark = false),
            StyleAssets.ColorTheme("colors-b", "B", isDark = true),
        )
        assertEquals("colors-b", resolve(followSystem = true, systemDark = true, themes = tiny))
        assertEquals("colors-a", resolve(followSystem = true, systemDark = false, themes = tiny))
    }
}
