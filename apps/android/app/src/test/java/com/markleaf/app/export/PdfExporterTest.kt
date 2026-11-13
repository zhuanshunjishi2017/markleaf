package com.markleaf.app.export

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class PdfExporterTest {
    @Test
    fun `standard margins are millimeters rather than inches`() {
        val attrs = PdfExporter.attributes(false, MARGIN_PRESETS.first())
        assertEquals(591, attrs.minMargins!!.leftMils)
        assertEquals(709, attrs.minMargins!!.topMils)
        assertEquals(591, attrs.minMargins!!.rightMils)
        assertEquals(709, attrs.minMargins!!.bottomMils)
    }

    @Test
    fun `narrow wide and no margin presets leave usable paper area`() {
        for ((id, expected) in listOf("narrow" to 394, "wide" to 787, "none" to 0)) {
            val attrs = PdfExporter.attributes(false, MARGIN_PRESETS.first { it.id == id })
            assertEquals(id, expected, attrs.minMargins!!.leftMils)
            assertTrue(attrs.mediaSize!!.widthMils > attrs.minMargins!!.leftMils + attrs.minMargins!!.rightMils)
        }
    }

    @Test
    fun `landscape swaps A4 dimensions without changing margins`() {
        val attrs = PdfExporter.attributes(true, MARGIN_PRESETS.first())
        assertEquals(11690, attrs.mediaSize!!.widthMils)
        assertEquals(8270, attrs.mediaSize!!.heightMils)
        assertEquals(591, attrs.minMargins!!.leftMils)
    }
}
