// M1 最小消息子集。protocol.ts 是唯一真源;M2(Task 8)以 fixtures 契约测试
// 扩展为 12+31 全集。此处类型只为打通 M1 握手(ready → applyStyles →
// stylesApplied → loadDocument → documentLoaded)。
use serde::{Deserialize, Serialize};

pub const PROTOCOL_VERSION: u32 = 1;

/// 宿主→编辑器消息(M1:applyStyles / loadDocument 两个构造器)。
#[derive(Debug, Clone, Serialize)]
pub struct HostMessage {
    #[serde(rename = "protocolVersion")]
    pub protocol_version: u32,
    #[serde(rename = "type")]
    pub message_type: &'static str,
    #[serde(rename = "documentId")]
    pub document_id: String,
    pub revision: i64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub request_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub payload: Option<serde_json::Value>,
}

impl HostMessage {
    pub fn apply_styles(document_id: &str, payload: serde_json::Value) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type: "applyStyles",
            document_id: document_id.to_string(),
            revision: 0,
            request_id: None,
            payload: Some(payload),
        }
    }

    pub fn load_document(document_id: &str, payload: serde_json::Value) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            message_type: "loadDocument",
            document_id: document_id.to_string(),
            revision: 0,
            request_id: None,
            payload: Some(payload),
        }
    }
}

/// 编辑器→宿主消息。M1 已知子集之外保留原始 type(M2 全集化)。
#[derive(Debug, Clone, PartialEq)]
pub enum EditorMessage {
    Ready,
    StylesApplied,
    DocumentLoaded { document_id: String, revision: i64 },
    Other(String),
}

#[derive(Debug)]
pub enum ParseError {
    Json(serde_json::Error),
    VersionMismatch { got: u32 },
}

impl std::fmt::Display for ParseError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ParseError::Json(err) => write!(f, "JSON 解析失败:{err}"),
            ParseError::VersionMismatch { got } => {
                write!(f, "protocolVersion 不匹配:期望 {PROTOCOL_VERSION},收到 {got}")
            }
        }
    }
}

pub fn parse_editor_message(json: &str) -> Result<EditorMessage, ParseError> {
    #[derive(Deserialize)]
    struct Raw {
        #[serde(rename = "protocolVersion")]
        protocol_version: u32,
        #[serde(rename = "type")]
        message_type: String,
        #[serde(rename = "documentId", default)]
        document_id: String,
        #[serde(default)]
        revision: i64,
    }
    let raw: Raw = serde_json::from_str(json).map_err(ParseError::Json)?;
    if raw.protocol_version != PROTOCOL_VERSION {
        return Err(ParseError::VersionMismatch { got: raw.protocol_version });
    }
    Ok(match raw.message_type.as_str() {
        "ready" => EditorMessage::Ready,
        "stylesApplied" => EditorMessage::StylesApplied,
        "documentLoaded" => EditorMessage::DocumentLoaded {
            document_id: raw.document_id,
            revision: raw.revision,
        },
        other => EditorMessage::Other(other.to_string()),
    })
}
