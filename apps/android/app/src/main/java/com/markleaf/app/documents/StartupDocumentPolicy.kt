package com.markleaf.app.documents

/** Startup content policy shared by the Android host's ready handshake. */
data class StartupDocument(
    val markdown: String,
    val documentType: String,
)

object StartupDocumentPolicy {
    /** A normal launch is a clean untitled Markdown document; recovery wins when present. */
    fun initialDocument(crashRestoreMarkdown: String?): StartupDocument =
        StartupDocument(markdown = crashRestoreMarkdown ?: "", documentType = "markdown")
}
