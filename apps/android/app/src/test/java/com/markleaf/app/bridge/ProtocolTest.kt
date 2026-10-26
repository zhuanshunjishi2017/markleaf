package com.markleaf.app.bridge

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 协议契约测试：样例 JSON 逐字段对照 packages/editor-web/src/protocol.ts。
 * 每种消息至少一条真实样例；宿主/编辑器两侧双向可解析。
 */
class ProtocolTest {

    // ---- 宿主 → 编辑器 ----

    @Test
    fun `parses loadDocument with payload`() {
        val json = """
            {"protocolVersion":1,"type":"loadDocument","documentId":"doc-1","revision":3,
             "payload":{"markdown":"# 标题\n\n正文","readOnly":false,"documentType":"markdown"}}
        """.trimIndent()
        val message = parseHostMessage(json)
        assertTrue(message is HostMessage.LoadDocument)
        message as HostMessage.LoadDocument
        assertEquals(PROTOCOL_VERSION, 1)
        assertEquals("doc-1", message.documentId)
        assertEquals(3L, message.revision)
        assertEquals("markdown", message.payload.documentType)
    }

    @Test
    fun `parses command with command name and text`() {
        val json = """
            {"protocolVersion":1,"type":"command","documentId":"doc-1","revision":3,
             "requestId":"req-9","payload":{"command":"insertTable","text":"3"}}
        """.trimIndent()
        val message = parseHostMessage(json) as HostMessage.Command
        assertEquals("req-9", message.requestId)
        assertEquals("insertTable", message.payload.command)
        assertEquals("3", message.payload.text)
    }

    @Test
    fun `parses applyStyles with css fields`() {
        val json = """
            {"protocolVersion":1,"type":"applyStyles","documentId":"doc-1","revision":0,
             "payload":{"baseCss":"body{}","frontendFormatMenu":true}}
        """.trimIndent()
        val message = parseHostMessage(json) as HostMessage.ApplyStyles
        assertEquals("body{}", message.payload.baseCss)
        assertEquals(true, message.payload.frontendFormatMenu)
    }

    @Test
    fun `parses every remaining host message type`() {
        val types = listOf(
            "setDocumentType", "requestSnapshot", "localizeFindBar",
            "unsafeEmphasisResponse", "restoreViewport", "markSaved",
            "refreshOutline", "codeFormatResult", "setCodeFormatterSettings",
        )
        for (type in types) {
            val json = """{"protocolVersion":1,"type":"$type","documentId":"d","revision":0}"""
            val message = parseHostMessage(json)
            assertTrue("type $type 应解析为宿主消息", message != null)
            assertEquals(type, requireNotNull(message).type)
        }
    }

    @Test
    fun `rejects unknown host message type`() {
        assertNull(parseHostMessage("""{"protocolVersion":1,"type":"teleport","documentId":"d","revision":0}"""))
    }

    @Test
    fun `rejects wrong protocol version`() {
        assertNull(parseHostMessage("""{"protocolVersion":2,"type":"markSaved","documentId":"d","revision":0}"""))
    }

    // ---- 编辑器 → 宿主 ----

    @Test
    fun `parses ready and encodes round trip`() {
        val json = """{"protocolVersion":1,"type":"ready","documentId":"d","revision":0}"""
        val message = parseEditorMessage(json) as EditorMessage.Ready
        val roundTrip = parseEditorMessage(encodeEditorMessage(message)) as EditorMessage.Ready
        assertEquals("d", roundTrip.documentId)
        assertEquals(0L, roundTrip.revision)
        assertEquals("ready", roundTrip.type)
    }

    @Test
    fun `parses documentLoaded and dirtyChanged`() {
        val loaded = parseEditorMessage(
            """{"protocolVersion":1,"type":"documentLoaded","documentId":"d","revision":1}""",
        )
        assertTrue(loaded is EditorMessage.DocumentLoaded)

        val dirty = parseEditorMessage(
            """{"protocolVersion":1,"type":"dirtyChanged","documentId":"d","revision":2,"payload":{"dirty":true}}""",
        )
        assertTrue(dirty is EditorMessage.DirtyChanged)
        assertEquals(true, (dirty as EditorMessage.DirtyChanged).payload.dirty)
    }

    @Test
    fun `parses snapshot with markdown payload`() {
        val json = """
            {"protocolVersion":1,"type":"snapshot","documentId":"d","revision":4,
             "requestId":"req-1","payload":{"markdown":"正文","selection":{"from":1,"to":2}}}
        """.trimIndent()
        val message = parseEditorMessage(json) as EditorMessage.Snapshot
        assertEquals("req-1", message.requestId)
        assertEquals("正文", message.payload.markdown)
    }

    @Test
    fun `parses commandStateChanged with actions map`() {
        val json = """
            {"protocolVersion":1,"type":"commandStateChanged","documentId":"d","revision":5,
             "payload":{"canUndo":true,"sourceMode":false,"readOnly":false,
                        "actions":{"toggleBold":{"enabled":true,"checked":false}}}}
        """.trimIndent()
        val message = parseEditorMessage(json) as EditorMessage.CommandStateChanged
        assertTrue(message.payload.canUndo)
        assertEquals(1, message.payload.actions.size)
        assertTrue(message.payload.actions.getValue("toggleBold").enabled)
    }

    @Test
    fun `parses contextMenuRequested with coordinates`() {
        val json = """
            {"protocolVersion":1,"type":"contextMenuRequested","documentId":"d","revision":6,
             "payload":{"clientX":120.5,"clientY":80.0,"menuHeight":35,
                        "canStartFormatPainter":true,"formatPainterArmed":false,"readOnly":false}}
        """.trimIndent()
        val message = parseEditorMessage(json) as EditorMessage.ContextMenuRequested
        assertEquals(120.5, message.payload.clientX, 0.001)
        assertEquals(35.0, message.payload.menuHeight, 0.001)
    }

    @Test
    fun `parses every remaining editor message type`() {
        val types = listOf(
            "stylesApplied", "commandResult", "selectionChanged", "editorStatusChanged",
            "blockMenuRequested", "codeBlockLanguageRequested", "copyCodeBlockRequested",
            "codeFormatRequested", "openLink", "dropFiles", "pasteImage",
            "outlineChanged", "outlineSelectionChanged", "requestSave",
            "selectionExport", "exportContent", "findResult", "zoomWheel",
            "unsafeEmphasisRequested", "typographyProjectionChanged", "typographyTrace",
            "error",
        )
        for (type in types) {
            val json = """{"protocolVersion":1,"type":"$type","documentId":"d","revision":0}"""
            val message = parseEditorMessage(json)
            assertTrue("type $type 应解析为编辑器消息", message != null)
            assertEquals(type, requireNotNull(message).type)
        }
    }

    @Test
    fun `rejects oversized message`() {
        val huge = "x".repeat(MAX_MESSAGE_BYTES + 1)
        assertThrows(MessageTooLargeException::class.java) {
            parseHostMessage("""{"protocolVersion":1,"type":"markSaved","documentId":"$huge","revision":0}""")
        }
    }

    @Test
    fun `encodes host message to json string`() {
        val encoded = encodeHostMessage(HostMessage.MarkSaved(documentId = "d", revision = 9))
        val roundTrip = parseHostMessage(encoded) as HostMessage.MarkSaved
        assertEquals(9L, roundTrip.revision)
    }
}
