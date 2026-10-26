package com.markleaf.app.bridge

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long

/** 与 packages/editor-web/src/protocol.ts 保持一致的协议常量。 */
const val PROTOCOL_VERSION = 1
const val MAX_MESSAGE_BYTES = 16 * 1024 * 1024

class MessageTooLargeException(message: String) : IllegalArgumentException(message)

/**
 * protocol.ts 的 Kotlin 镜像：宿主→编辑器消息。
 * payload 保持 JsonElement 原样透传（宿主侧大多只做转发或取顶层字段）。
 */
@Serializable
sealed interface HostMessage {
    val type: String
    val documentId: String
    val revision: Long
    val requestId: String?

    @Serializable
    @SerialName("loadDocument")
    data class LoadDocument(
        override val documentId: String,
        override val revision: Long,
        val payload: LoadDocumentPayload,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "loadDocument"

        @Serializable
        data class LoadDocumentPayload(
            val markdown: String = "",
            val readOnly: Boolean = false,
            val documentType: String = "markdown",
            val documentPath: String? = null,
            val initialDirty: Boolean = false,
            val visualSelection: JsonElement? = null,
            val sourceSelection: JsonElement? = null,
            val scrollTop: Double? = null,
            val readingAnchor: JsonElement? = null,
        )
    }

    @Serializable
    @SerialName("setDocumentType")
    data class SetDocumentType(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "setDocumentType"
    }

    @Serializable
    @SerialName("requestSnapshot")
    data class RequestSnapshot(
        override val documentId: String,
        override val revision: Long,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "requestSnapshot"
    }

    @Serializable
    @SerialName("command")
    data class Command(
        override val documentId: String,
        override val revision: Long,
        val payload: CommandPayload,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "command"

        @Serializable
        data class CommandPayload(
            val command: String? = null,
            val text: String? = null,
            val html: String? = null,
            val clientX: Double? = null,
            val clientY: Double? = null,
        ) {
            companion object {
                /** command 字段缺省时前端视为无操作；未知字段由 ignoreUnknownKeys 丢弃。 */
                fun from(json: JsonObject): CommandPayload {
                    fun str(name: String) = json[name]?.jsonPrimitive?.content
                    fun num(name: String) = json[name]?.jsonPrimitive?.double
                    return CommandPayload(
                        command = str("command"),
                        text = str("text"),
                        html = str("html"),
                        clientX = num("clientX"),
                        clientY = num("clientY"),
                    )
                }
            }
        }
    }

    @Serializable
    @SerialName("applyStyles")
    data class ApplyStyles(
        override val documentId: String,
        override val revision: Long,
        val payload: ApplyStylesPayload = ApplyStylesPayload(),
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "applyStyles"

        @Serializable
        data class ApplyStylesPayload(
            val baseCss: String = "",
            val colorThemeCss: String? = null,
            val styles: JsonElement? = null,
            val activeStyle: String? = null,
            val frontendFormatMenu: Boolean? = null,
        )
    }

    @Serializable
    @SerialName("localizeFindBar")
    data class LocalizeFindBar(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "localizeFindBar"
    }

    @Serializable
    @SerialName("unsafeEmphasisResponse")
    data class UnsafeEmphasisResponse(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "unsafeEmphasisResponse"
    }

    @Serializable
    @SerialName("restoreViewport")
    data class RestoreViewport(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "restoreViewport"
    }

    @Serializable
    @SerialName("markSaved")
    data class MarkSaved(
        override val documentId: String,
        override val revision: Long,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "markSaved"
    }

    @Serializable
    @SerialName("refreshOutline")
    data class RefreshOutline(
        override val documentId: String,
        override val revision: Long,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "refreshOutline"
    }

    @Serializable
    @SerialName("codeFormatResult")
    data class CodeFormatResult(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "codeFormatResult"
    }

    @Serializable
    @SerialName("setCodeFormatterSettings")
    data class SetCodeFormatterSettings(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : HostMessage {
        override val type: String get() = "setCodeFormatterSettings"
    }

    companion object {
        /** protocol.ts isHostMessage 允许的 type 全集。 */
        val ALLOWED_TYPES: Set<String> = setOf(
            "loadDocument", "setDocumentType", "requestSnapshot", "command",
            "applyStyles", "localizeFindBar", "unsafeEmphasisResponse",
            "restoreViewport", "markSaved", "refreshOutline",
            "codeFormatResult", "setCodeFormatterSettings",
        )
    }
}

/** 编辑器→宿主消息。payload 为强类型或 JsonElement，读取方按需取字段。 */
@Serializable
sealed interface EditorMessage {
    val type: String
    val documentId: String
    val revision: Long
    val requestId: String?

    @Serializable
    @SerialName("ready")
    data class Ready(
        override val documentId: String,
        override val revision: Long,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "ready"
    }

    @Serializable
    @SerialName("stylesApplied")
    data class StylesApplied(
        override val documentId: String,
        override val revision: Long,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "stylesApplied"
    }

    @Serializable
    @SerialName("documentLoaded")
    data class DocumentLoaded(
        override val documentId: String,
        override val revision: Long,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "documentLoaded"
    }

    @Serializable
    @SerialName("commandResult")
    data class CommandResult(
        override val documentId: String,
        override val revision: Long,
        val payload: CommandResultPayload = CommandResultPayload(),
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "commandResult"

        @Serializable
        data class CommandResultPayload(val success: Boolean = false)
    }

    @Serializable
    @SerialName("dirtyChanged")
    data class DirtyChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: DirtyPayload = DirtyPayload(),
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "dirtyChanged"

        @Serializable
        data class DirtyPayload(val dirty: Boolean = false)
    }

    @Serializable
    @SerialName("snapshot")
    data class Snapshot(
        override val documentId: String,
        override val revision: Long,
        val payload: SnapshotPayload = SnapshotPayload(),
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "snapshot"

        @Serializable
        data class SnapshotPayload(
            val markdown: String = "",
            val selection: JsonElement? = null,
        )
    }

    @Serializable
    @SerialName("selectionChanged")
    data class SelectionChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "selectionChanged"
    }

    @Serializable
    @SerialName("commandStateChanged")
    data class CommandStateChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: CommandStatePayload = CommandStatePayload(),
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "commandStateChanged"

        @Serializable
        data class CommandStatePayload(
            val canUndo: Boolean = false,
            val sourceMode: Boolean = false,
            val readOnly: Boolean = false,
            val actions: Map<String, ActionState> = emptyMap(),
        )

        @Serializable
        data class ActionState(val enabled: Boolean = false, val checked: Boolean = false)
    }

    @Serializable
    @SerialName("contextMenuRequested")
    data class ContextMenuRequested(
        override val documentId: String,
        override val revision: Long,
        val payload: ContextMenuPayload = ContextMenuPayload(),
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "contextMenuRequested"

        @Serializable
        data class ContextMenuPayload(
            val clientX: Double = 0.0,
            val clientY: Double = 0.0,
            val menuHeight: Double = 0.0,
            val canStartFormatPainter: Boolean = false,
            val formatPainterArmed: Boolean = false,
            val readOnly: Boolean = false,
            val sourceMode: Boolean = false,
            val expandedSource: Boolean = false,
            val outsideDocument: Boolean = false,
        )
    }

    @Serializable
    @SerialName("blockMenuRequested")
    data class BlockMenuRequested(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "blockMenuRequested"
    }

    @Serializable
    @SerialName("codeBlockLanguageRequested")
    data class CodeBlockLanguageRequested(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "codeBlockLanguageRequested"
    }

    @Serializable
    @SerialName("copyCodeBlockRequested")
    data class CopyCodeBlockRequested(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "copyCodeBlockRequested"
    }

    @Serializable
    @SerialName("codeFormatRequested")
    data class CodeFormatRequested(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "codeFormatRequested"
    }

    @Serializable
    @SerialName("openLink")
    data class OpenLink(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "openLink"
    }

    @Serializable
    @SerialName("dropFiles")
    data class DropFiles(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "dropFiles"
    }

    @Serializable
    @SerialName("pasteImage")
    data class PasteImage(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "pasteImage"
    }

    @Serializable
    @SerialName("outlineChanged")
    data class OutlineChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "outlineChanged"
    }

    @Serializable
    @SerialName("outlineSelectionChanged")
    data class OutlineSelectionChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "outlineSelectionChanged"
    }

    @Serializable
    @SerialName("requestSave")
    data class RequestSave(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "requestSave"
    }

    @Serializable
    @SerialName("selectionExport")
    data class SelectionExport(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "selectionExport"
    }

    @Serializable
    @SerialName("exportContent")
    data class ExportContent(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "exportContent"
    }

    @Serializable
    @SerialName("findResult")
    data class FindResult(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "findResult"
    }

    @Serializable
    @SerialName("zoomWheel")
    data class ZoomWheel(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "zoomWheel"
    }

    @Serializable
    @SerialName("unsafeEmphasisRequested")
    data class UnsafeEmphasisRequested(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "unsafeEmphasisRequested"
    }

    @Serializable
    @SerialName("editorStatusChanged")
    data class EditorStatusChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "editorStatusChanged"
    }

    @Serializable
    @SerialName("typographyProjectionChanged")
    data class TypographyProjectionChanged(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "typographyProjectionChanged"
    }

    @Serializable
    @SerialName("typographyTrace")
    data class TypographyTrace(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "typographyTrace"
    }

    @Serializable
    @SerialName("error")
    data class EditorError(
        override val documentId: String,
        override val revision: Long,
        val payload: JsonElement? = null,
        override val requestId: String? = null,
    ) : EditorMessage {
        override val type: String get() = "error"
    }

    companion object {
        /** protocol.ts EditorMessage type 全集。 */
        val ALLOWED_TYPES: Set<String> = setOf(
            "ready", "stylesApplied", "documentLoaded", "commandResult",
            "dirtyChanged", "snapshot", "selectionChanged", "commandStateChanged",
            "editorStatusChanged", "contextMenuRequested", "blockMenuRequested",
            "codeBlockLanguageRequested", "copyCodeBlockRequested", "codeFormatRequested",
            "openLink", "dropFiles", "pasteImage", "findResult", "selectionExport",
            "exportContent", "zoomWheel", "unsafeEmphasisRequested",
            "outlineChanged", "outlineSelectionChanged", "requestSave",
            "typographyProjectionChanged", "typographyTrace", "error",
        )
    }
}

private val hostJson = Json {
    ignoreUnknownKeys = true
    classDiscriminator = "type"
}

private val editorJson = Json {
    ignoreUnknownKeys = true
    classDiscriminator = "type"
    encodeDefaults = true
}

/** 解析宿主消息（测试与桥一致性校验用）。未知 type / 版本不符返回 null。 */
fun parseHostMessage(json: String): HostMessage? {
    checkSize(json)
    val root = runCatching { Json.parseToJsonElement(json).jsonObject }.getOrNull() ?: return null
    val version = root["protocolVersion"]?.jsonPrimitive?.long ?: return null
    if (version != PROTOCOL_VERSION.toLong()) return null
    val type = root["type"]?.jsonPrimitive?.content ?: return null
    if (type !in HostMessage.ALLOWED_TYPES) return null
    return runCatching { hostJson.decodeFromString<HostMessage>(json) }.getOrNull()
}

/** 解析编辑器消息（前端→宿主方向的主要入口）。未知 type 返回 null（调用方记日志）。 */
fun parseEditorMessage(json: String): EditorMessage? {
    checkSize(json)
    val root = runCatching { Json.parseToJsonElement(json).jsonObject }.getOrNull() ?: return null
    val version = root["protocolVersion"]?.jsonPrimitive?.long ?: return null
    if (version != PROTOCOL_VERSION.toLong()) return null
    val type = root["type"]?.jsonPrimitive?.content ?: return null
    if (type !in EditorMessage.ALLOWED_TYPES) return null
    return runCatching { editorJson.decodeFromString<EditorMessage>(json) }.getOrNull()
}

fun encodeEditorMessage(message: EditorMessage): String {
    // 接口的 protocolVersion 是校验用常量，不进 @Serializable 数据体；编码时补写。
    val json = editorJson.encodeToString(message)
    return injectProtocolVersion(json)
}

fun encodeHostMessage(message: HostMessage): String {
    val json = hostJson.encodeToString(message)
    return injectProtocolVersion(json)
}

private fun injectProtocolVersion(json: String): String {
    if (json.contains("\"protocolVersion\"")) return json
    // 在第一个 { 后插入 protocolVersion 字段。
    return json.replaceFirst("{", "{\"protocolVersion\":$PROTOCOL_VERSION,")
}

private fun checkSize(json: String) {
    // 前端按 UTF-8 字节计量；Kotlin length 是 UTF-16 code unit 数，对含中文
    // payload 偏保守（更早拒绝），处于安全侧。
    if (json.length > MAX_MESSAGE_BYTES) {
        throw MessageTooLargeException("message exceeds $MAX_MESSAGE_BYTES bytes")
    }
}
