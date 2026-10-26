package com.markleaf.app.bridge

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** EditorBridge 行为契约：ready 握手时序、stale 过滤、JSON 转义。 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class EditorBridgeTest {

    private class RecordingPoster {
        val scripts = mutableListOf<String>()
        fun post(script: String) { scripts.add(script) }
    }

    private fun outgoingPayload(script: String): JSONObject? {
        // postToEditor 生成 window.postMessage(JSON.parse('…'), '*')；
        // 提取单引号内转义串并还原 JSON。
        val start = script.indexOf("JSON.parse('") + "JSON.parse('".length
        val end = script.lastIndexOf("')")
        if (start < 1 || end <= start) return null
        val escaped = script.substring(start, end)
        val unescaped = escaped
            .replace("\\\\", "\u0000")
            .replace("\\'", "'")
            .replace("\u0000", "\\")
        return JSONObject(unescaped)
    }

    @Test
    fun `buffers host messages until ready then flushes in order`() {
        val recorder = RecordingPoster()
        val session = EditorSessionState().apply { startDocument("doc-1") }
        val bridge = EditorBridge(recorder::post, session)
        bridge.attach()

        // ready 前的宿主消息应缓存
        bridge.post(
            HostMessage.ApplyStyles(
                documentId = "doc-1",
                revision = 0,
                payload = HostMessage.ApplyStyles.ApplyStylesPayload(baseCss = "body{}"),
            ),
        )
        assertTrue("ready 前不应有任何 evaluateJavascript", recorder.scripts.isEmpty())

        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"ready","documentId":"doc-1","revision":0}""",
        )

        // ready 后缓存按序 flush
        assertEquals(1, recorder.scripts.size)
        val flushed = outgoingPayload(recorder.scripts.first())
        assertEquals("applyStyles", flushed?.getString("type"))
    }

    @Test
    fun `ready handshake ignores foreign document`() {
        val recorder = RecordingPoster()
        val session = EditorSessionState().apply { startDocument("doc-1") }
        val bridge = EditorBridge(recorder::post, session)
        bridge.attach()

        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"ready","documentId":"other-doc","revision":0}""",
        )
        assertTrue(recorder.scripts.isEmpty())
    }

    @Test
    fun `stale editor messages are filtered and not delivered`() {
        val recorder = RecordingPoster()
        val session = EditorSessionState().apply { startDocument("doc-1") }
        var delivered = 0
        val bridge = EditorBridge(recorder::post, session) { delivered++ }
        bridge.attach()

        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"documentLoaded","documentId":"doc-1","revision":5}""",
        )
        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"dirtyChanged","documentId":"doc-1","revision":4,
                "payload":{"dirty":true}}""",
        )

        assertEquals(1, delivered)
    }

    @Test
    fun `unknown type is ignored without crash`() {
        val recorder = RecordingPoster()
        val session = EditorSessionState().apply { startDocument("doc-1") }
        var delivered = 0
        val bridge = EditorBridge(recorder::post, session) { delivered++ }
        bridge.attach()
        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"teleport","documentId":"doc-1","revision":1}""",
        )
        assertEquals(0, delivered)
    }

    @Test
    fun `payload with quotes and newlines survives escaping`() {
        val recorder = RecordingPoster()
        val session = EditorSessionState().apply { startDocument("doc-1") }
        val bridge = EditorBridge(recorder::post, session)
        bridge.attach()
        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"ready","documentId":"doc-1","revision":0}""",
        )

        val trickyMarkdown = "含'单引号'\n换行\\反斜杠"
        bridge.post(
            HostMessage.LoadDocument(
                documentId = "doc-1",
                revision = 0,
                payload = HostMessage.LoadDocument.LoadDocumentPayload(markdown = trickyMarkdown),
            ),
        )

        val payload = outgoingPayload(recorder.scripts.single())
        assertEquals(trickyMarkdown, payload?.getJSONObject("payload")?.getString("markdown"))
    }

    @Test
    fun `post uses protocol envelope for editor consumption`() {
        val recorder = RecordingPoster()
        val session = EditorSessionState().apply { startDocument("doc-1") }
        val bridge = EditorBridge(recorder::post, session)
        bridge.attach()
        bridge.onEditorJson(
            """{"protocolVersion":1,"type":"ready","documentId":"doc-1","revision":0}""",
        )

        bridge.post(HostMessage.MarkSaved(documentId = "doc-1", revision = 1))
        val payload = outgoingPayload(recorder.scripts.last())
        assertEquals(1, payload?.getInt("protocolVersion"))
        assertEquals("markSaved", payload?.getString("type"))
    }
}
