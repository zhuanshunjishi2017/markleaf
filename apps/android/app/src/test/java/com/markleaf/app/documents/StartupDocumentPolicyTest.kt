package com.markleaf.app.documents

import org.junit.Assert.assertEquals
import org.junit.Test

class StartupDocumentPolicyTest {
    @Test
    fun `cold start uses blank markdown instead of bundled welcome sample`() {
        val document = StartupDocumentPolicy.initialDocument(crashRestoreMarkdown = null)

        assertEquals("", document.markdown)
        assertEquals("markdown", document.documentType)
    }

    @Test
    fun `render process recovery keeps recovered markdown`() {
        val document = StartupDocumentPolicy.initialDocument("recovered content")

        assertEquals("recovered content", document.markdown)
        assertEquals("markdown", document.documentType)
    }
}
