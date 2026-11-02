package com.markleaf.app

import org.junit.Assert.assertEquals
import org.junit.Test

class L10nTest {

    @Test
    fun `zh-Hans returns key as-is`() {
        L10n.language = "zh-Hans"
        assertEquals("首选项", L10n.t("首选项"))
    }

    @Test
    fun `en translates shell strings`() {
        L10n.language = "en"
        assertEquals("Preferences", L10n.t("首选项"))
        assertEquals("Appearance", L10n.t("外观"))
        assertEquals("File", L10n.t("文件"))
        assertEquals("Save", L10n.t("保存"))
        assertEquals("Language", L10n.t("显示语言"))
    }

    @Test
    fun `format fills arguments after translation`() {
        L10n.language = "en"
        assertEquals("Saved As foo.md", L10n.f("已另存为 %s", "foo.md"))
        L10n.language = "zh-Hans"
        assertEquals("已另存为 foo.md", L10n.f("已另存为 %s", "foo.md"))
    }
}
