// protocol.ts 的 Rust 镜像(唯一协议真源见 packages/editor-web/src/protocol.ts)。
// 形态裁决:protocol.ts 本身是"信封 + type 字段"结构,故 Rust 侧用
//   Envelope { protocolVersion, type, documentId, revision, requestId?, payload? }
// + 独立类型枚举,而非 44 个变体各带公共字段的 enum——payload 透传 Value
// (对齐 Android Protocol.kt 的 JsonElement 透传),往返要求 Value 级相等。
// 编辑器消息全集 32 种、宿主 12 种,由 tests/protocol_sync.rs 对 protocol.ts
// 源码强制同步(数量在 plan 中误记 31,以同步测试为准)。
use serde::{Deserialize, Serialize};

pub const PROTOCOL_VERSION: u32 = 1;
pub const MAX_MESSAGE_BYTES: usize = 16 * 1024 * 1024;

// ---- 宿主 → 编辑器(12) ----

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum HostMessageType {
    #[serde(rename = "loadDocument")]
    LoadDocument,
    #[serde(rename = "setDocumentType")]
    SetDocumentType,
    #[serde(rename = "requestSnapshot")]
    RequestSnapshot,
    #[serde(rename = "command")]
    Command,
    #[serde(rename = "applyStyles")]
    ApplyStyles,
    #[serde(rename = "localizeFindBar")]
    LocalizeFindBar,
    #[serde(rename = "unsafeEmphasisResponse")]
    UnsafeEmphasisResponse,
    #[serde(rename = "restoreViewport")]
    RestoreViewport,
    #[serde(rename = "markSaved")]
    MarkSaved,
    #[serde(rename = "refreshOutline")]
    RefreshOutline,
    #[serde(rename = "codeFormatResult")]
    CodeFormatResult,
    #[serde(rename = "setCodeFormatterSettings")]
    SetCodeFormatterSettings,
}

impl HostMessageType {
    pub fn all_names() -> &'static [&'static str] {
        &[
            "loadDocument",
            "setDocumentType",
            "requestSnapshot",
            "command",
            "applyStyles",
            "localizeFindBar",
            "unsafeEmphasisResponse",
            "restoreViewport",
            "markSaved",
            "refreshOutline",
            "codeFormatResult",
            "setCodeFormatterSettings",
        ]
    }

    pub fn name(&self) -> &'static str {
        match self {
            HostMessageType::LoadDocument => "loadDocument",
            HostMessageType::SetDocumentType => "setDocumentType",
            HostMessageType::RequestSnapshot => "requestSnapshot",
            HostMessageType::Command => "command",
            HostMessageType::ApplyStyles => "applyStyles",
            HostMessageType::LocalizeFindBar => "localizeFindBar",
            HostMessageType::UnsafeEmphasisResponse => "unsafeEmphasisResponse",
            HostMessageType::RestoreViewport => "restoreViewport",
            HostMessageType::MarkSaved => "markSaved",
            HostMessageType::RefreshOutline => "refreshOutline",
            HostMessageType::CodeFormatResult => "codeFormatResult",
            HostMessageType::SetCodeFormatterSettings => "setCodeFormatterSettings",
        }
    }
}

// ---- 编辑器 → 宿主(32) ----

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum EditorMessageType {
    #[serde(rename = "ready")]
    Ready,
    #[serde(rename = "stylesApplied")]
    StylesApplied,
    #[serde(rename = "documentLoaded")]
    DocumentLoaded,
    #[serde(rename = "commandResult")]
    CommandResult,
    #[serde(rename = "dirtyChanged")]
    DirtyChanged,
    #[serde(rename = "snapshot")]
    Snapshot,
    #[serde(rename = "selectionChanged")]
    SelectionChanged,
    #[serde(rename = "commandStateChanged")]
    CommandStateChanged,
    #[serde(rename = "editorStatusChanged")]
    EditorStatusChanged,
    #[serde(rename = "contextMenuRequested")]
    ContextMenuRequested,
    #[serde(rename = "blockMenuRequested")]
    BlockMenuRequested,
    #[serde(rename = "codeBlockLanguageRequested")]
    CodeBlockLanguageRequested,
    #[serde(rename = "copyCodeBlockRequested")]
    CopyCodeBlockRequested,
    #[serde(rename = "outlineChanged")]
    OutlineChanged,
    #[serde(rename = "outlineSelectionChanged")]
    OutlineSelectionChanged,
    #[serde(rename = "requestSave")]
    RequestSave,
    #[serde(rename = "openLink")]
    OpenLink,
    #[serde(rename = "dropFiles")]
    DropFiles,
    #[serde(rename = "pasteImage")]
    PasteImage,
    #[serde(rename = "findResult")]
    FindResult,
    #[serde(rename = "selectionExport")]
    SelectionExport,
    #[serde(rename = "mermaidImageExport")]
    MermaidImageExport,
    #[serde(rename = "exportContent")]
    ExportContent,
    #[serde(rename = "zoomWheel")]
    ZoomWheel,
    #[serde(rename = "unsafeEmphasisRequested")]
    UnsafeEmphasisRequested,
    #[serde(rename = "typographyProjectionChanged")]
    TypographyProjectionChanged,
    #[serde(rename = "typographyTrace")]
    TypographyTrace,
    #[serde(rename = "footnoteDefinitionMissing")]
    FootnoteDefinitionMissing,
    #[serde(rename = "footnoteReferenceMissing")]
    FootnoteReferenceMissing,
    #[serde(rename = "codeFormatRequested")]
    CodeFormatRequested,
    #[serde(rename = "codeFormatResult")]
    CodeFormatResult,
    #[serde(rename = "error")]
    Error,
}

impl EditorMessageType {
    pub fn all_names() -> &'static [&'static str] {
        &[
            "ready",
            "stylesApplied",
            "documentLoaded",
            "commandResult",
            "dirtyChanged",
            "snapshot",
            "selectionChanged",
            "commandStateChanged",
            "editorStatusChanged",
            "contextMenuRequested",
            "blockMenuRequested",
            "codeBlockLanguageRequested",
            "copyCodeBlockRequested",
            "outlineChanged",
            "outlineSelectionChanged",
            "requestSave",
            "openLink",
            "dropFiles",
            "pasteImage",
            "findResult",
            "selectionExport",
            "mermaidImageExport",
            "exportContent",
            "zoomWheel",
            "unsafeEmphasisRequested",
            "typographyProjectionChanged",
            "typographyTrace",
            "footnoteDefinitionMissing",
            "footnoteReferenceMissing",
            "codeFormatRequested",
            "codeFormatResult",
            "error",
        ]
    }

    pub fn name(&self) -> &'static str {
        match self {
            EditorMessageType::Ready => "ready",
            EditorMessageType::StylesApplied => "stylesApplied",
            EditorMessageType::DocumentLoaded => "documentLoaded",
            EditorMessageType::CommandResult => "commandResult",
            EditorMessageType::DirtyChanged => "dirtyChanged",
            EditorMessageType::Snapshot => "snapshot",
            EditorMessageType::SelectionChanged => "selectionChanged",
            EditorMessageType::CommandStateChanged => "commandStateChanged",
            EditorMessageType::EditorStatusChanged => "editorStatusChanged",
            EditorMessageType::ContextMenuRequested => "contextMenuRequested",
            EditorMessageType::BlockMenuRequested => "blockMenuRequested",
            EditorMessageType::CodeBlockLanguageRequested => "codeBlockLanguageRequested",
            EditorMessageType::CopyCodeBlockRequested => "copyCodeBlockRequested",
            EditorMessageType::OutlineChanged => "outlineChanged",
            EditorMessageType::OutlineSelectionChanged => "outlineSelectionChanged",
            EditorMessageType::RequestSave => "requestSave",
            EditorMessageType::OpenLink => "openLink",
            EditorMessageType::DropFiles => "dropFiles",
            EditorMessageType::PasteImage => "pasteImage",
            EditorMessageType::FindResult => "findResult",
            EditorMessageType::SelectionExport => "selectionExport",
            EditorMessageType::MermaidImageExport => "mermaidImageExport",
            EditorMessageType::ExportContent => "exportContent",
            EditorMessageType::ZoomWheel => "zoomWheel",
            EditorMessageType::UnsafeEmphasisRequested => "unsafeEmphasisRequested",
            EditorMessageType::TypographyProjectionChanged => "typographyProjectionChanged",
            EditorMessageType::TypographyTrace => "typographyTrace",
            EditorMessageType::FootnoteDefinitionMissing => "footnoteDefinitionMissing",
            EditorMessageType::FootnoteReferenceMissing => "footnoteReferenceMissing",
            EditorMessageType::CodeFormatRequested => "codeFormatRequested",
            EditorMessageType::CodeFormatResult => "codeFormatResult",
            EditorMessageType::Error => "error",
        }
    }
}

// ---- 信封 ----

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct HostEnvelope {
    #[serde(rename = "protocolVersion")]
    pub protocol_version: u32,
    #[serde(rename = "type")]
    pub message_type: HostMessageType,
    #[serde(rename = "documentId")]
    pub document_id: String,
    pub revision: i64,
    #[serde(rename = "requestId", skip_serializing_if = "Option::is_none")]
    pub request_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub payload: Option<serde_json::Value>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EditorEnvelope {
    #[serde(rename = "protocolVersion")]
    pub protocol_version: u32,
    #[serde(rename = "type")]
    pub message_type: EditorMessageType,
    #[serde(rename = "documentId", default)]
    pub document_id: String,
    #[serde(default)]
    pub revision: i64,
    #[serde(rename = "requestId", skip_serializing_if = "Option::is_none")]
    pub request_id: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub payload: Option<serde_json::Value>,
}

impl HostEnvelope {
    pub fn message_type_name(&self) -> &'static str {
        self.message_type.name()
    }

    // ---- 宿主侧构造器(应用层/冒烟使用;字段形态对齐 macOS EditorSession) ----

    fn new(message_type: HostMessageType, document_id: &str, payload: Option<serde_json::Value>) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type,
            document_id: document_id.to_string(),
            revision: 0,
            request_id: None,
            payload,
        }
    }

    pub fn apply_styles(document_id: &str, payload: serde_json::Value) -> Self {
        Self::new(HostMessageType::ApplyStyles, document_id, Some(payload))
    }

    pub fn load_document(document_id: &str, payload: serde_json::Value) -> Self {
        Self::new(HostMessageType::LoadDocument, document_id, Some(payload))
    }

    pub fn command(document_id: &str, revision: i64, payload: serde_json::Value) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type: HostMessageType::Command,
            document_id: document_id.to_string(),
            revision,
            request_id: None,
            payload: Some(payload),
        }
    }

    pub fn request_snapshot(document_id: &str, revision: i64, request_id: &str) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type: HostMessageType::RequestSnapshot,
            document_id: document_id.to_string(),
            revision,
            request_id: Some(request_id.to_string()),
            payload: None,
        }
    }

    pub fn mark_saved(document_id: &str, revision: i64) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type: HostMessageType::MarkSaved,
            document_id: document_id.to_string(),
            revision,
            request_id: None,
            payload: None,
        }
    }

    pub fn refresh_outline(document_id: &str, revision: i64) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type: HostMessageType::RefreshOutline,
            document_id: document_id.to_string(),
            revision,
            request_id: None,
            payload: None,
        }
    }
}

impl EditorEnvelope {
    pub fn message_type_name(&self) -> &'static str {
        self.message_type.name()
    }
}

// ---- 解析与编码 ----

#[derive(Debug)]
pub enum ProtocolError {
    Json(serde_json::Error),
    Malformed(String),
    VersionMismatch { got: u32 },
    UnknownType(String),
    TooLarge,
}

impl std::fmt::Display for ProtocolError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ProtocolError::Json(err) => write!(f, "JSON 解析失败:{err}"),
            ProtocolError::Malformed(detail) => write!(f, "消息形态不合法:{detail}"),
            ProtocolError::VersionMismatch { got } => {
                write!(f, "protocolVersion 不匹配:期望 {PROTOCOL_VERSION},收到 {got}")
            }
            ProtocolError::UnknownType(name) => write!(f, "未知消息类型:{name}"),
            ProtocolError::TooLarge => {
                write!(f, "消息超过 {} 字节上限", MAX_MESSAGE_BYTES)
            }
        }
    }
}

impl std::error::Error for ProtocolError {}

fn check_size(value: &serde_json::Value) -> Result<(), ProtocolError> {
    let serialized = serde_json::to_string(value).map_err(ProtocolError::Json)?;
    if serialized.into_bytes().len() > MAX_MESSAGE_BYTES {
        return Err(ProtocolError::TooLarge);
    }
    Ok(())
}

fn check_version(value: &serde_json::Value) -> Result<u32, ProtocolError> {
    let version = value
        .get("protocolVersion")
        .and_then(|v| v.as_u64())
        .ok_or_else(|| ProtocolError::Malformed("缺 protocolVersion".into()))?;
    if version != PROTOCOL_VERSION as u64 {
        return Err(ProtocolError::VersionMismatch { got: version as u32 });
    }
    Ok(version as u32)
}

fn check_type_name(value: &serde_json::Value) -> Result<String, ProtocolError> {
    let name = value
        .get("type")
        .and_then(|v| v.as_str())
        .ok_or_else(|| ProtocolError::Malformed("缺 type 字符串".into()))?;
    Ok(name.to_string())
}

pub fn parse_host_message_value(value: serde_json::Value) -> Result<HostEnvelope, ProtocolError> {
    check_size(&value)?;
    check_version(&value)?;
    let type_name = check_type_name(&value)?;
    if !HostMessageType::all_names().contains(&type_name.as_str()) {
        return Err(ProtocolError::UnknownType(type_name));
    }
    let envelope: HostEnvelope =
        serde_json::from_value(value).map_err(ProtocolError::Json)?;
    // type 名称已在枚举中校验;此处防御 documentId 空值(isHostMessage 要求非空)。
    if envelope.document_id.is_empty() {
        return Err(ProtocolError::Malformed("documentId 不能为空".into()));
    }
    Ok(envelope)
}

pub fn parse_editor_message_value(value: serde_json::Value) -> Result<EditorEnvelope, ProtocolError> {
    check_size(&value)?;
    check_version(&value)?;
    let type_name = check_type_name(&value)?;
    if !EditorMessageType::all_names().contains(&type_name.as_str()) {
        return Err(ProtocolError::UnknownType(type_name));
    }
    let envelope: EditorEnvelope =
        serde_json::from_value(value).map_err(ProtocolError::Json)?;
    Ok(envelope)
}

pub fn encode_host_message_value(envelope: &HostEnvelope) -> serde_json::Value {
    serde_json::to_value(envelope).expect("HostEnvelope 序列化不失败")
}

pub fn encode_editor_message_value(envelope: &EditorEnvelope) -> serde_json::Value {
    serde_json::to_value(envelope).expect("EditorEnvelope 序列化不失败")
}

// 字符串便捷入口(桥的 script message 入口是文本)。
pub fn parse_editor_message(
    json: &str,
) -> Result<EditorEnvelope, ProtocolError> {
    let value: serde_json::Value =
        serde_json::from_str(json).map_err(|err| ProtocolError::Malformed(err.to_string()))?;
    parse_editor_message_value(value)
}
