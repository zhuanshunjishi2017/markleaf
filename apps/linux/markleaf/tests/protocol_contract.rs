// 协议契约测试:44 条 fixture 与 protocol.rs 双向往返。
// fixture 从 protocol.ts 消息全集与 Android Protocol.kt / macOS EditorSession
// 的真实载荷形态抄录;payload 一律透传 Value,往返要求 Value 级相等。
use markleaf::bridge::protocol::{
    parse_host_message_value, parse_editor_message_value, encode_host_message_value,
    encode_editor_message_value, ProtocolError, EditorMessageType, HostMessageType,
};

fn fixture(side: &str, name: &str) -> serde_json::Value {
    let path = format!(
        "{}/tests/fixtures/protocol/{}_{}.json",
        env!("CARGO_MANIFEST_DIR"),
        side,
        name
    );
    let text = std::fs::read_to_string(&path)
        .unwrap_or_else(|err| panic!("fixture 缺失 {path}: {err}"));
    serde_json::from_str(&text).expect("fixture 是合法 JSON")
}

#[test]
fn host_fixtures_round_trip() {
    for name in [
        "loadDocument", "setDocumentType", "requestSnapshot", "command",
        "applyStyles", "localizeFindBar", "unsafeEmphasisResponse",
        "restoreViewport", "markSaved", "refreshOutline",
        "codeFormatResult", "setCodeFormatterSettings",
    ] {
        let original = fixture("host", name);
        let envelope = parse_host_message_value(original.clone())
            .unwrap_or_else(|err| panic!("{name} 解析失败:{err}"));
        assert_eq!(envelope.message_type_name(), name, "{name} type 名称不符");
        let re_encoded = encode_host_message_value(&envelope);
        assert_eq!(re_encoded, original, "{name} 往返不等");
    }
}

#[test]
fn editor_fixtures_round_trip() {
    for name in [
        "ready", "stylesApplied", "documentLoaded", "commandResult",
        "dirtyChanged", "snapshot", "selectionChanged", "commandStateChanged",
        "editorStatusChanged", "contextMenuRequested", "blockMenuRequested",
        "codeBlockLanguageRequested", "copyCodeBlockRequested", "outlineChanged",
        "outlineSelectionChanged", "requestSave", "openLink", "dropFiles",
        "pasteImage", "findResult", "selectionExport", "mermaidImageExport",
        "exportContent", "zoomWheel", "unsafeEmphasisRequested",
        "typographyProjectionChanged", "typographyTrace",
        "footnoteDefinitionMissing", "footnoteReferenceMissing",
        "codeFormatRequested", "codeFormatResult", "error",
    ] {
        let original = fixture("editor", name);
        let envelope = parse_editor_message_value(original.clone())
            .unwrap_or_else(|err| panic!("{name} 解析失败:{err}"));
        assert_eq!(envelope.message_type_name(), name, "{name} type 名称不符");
        let re_encoded = encode_editor_message_value(&envelope);
        assert_eq!(re_encoded, original, "{name} 往返不等");
    }
}

#[test]
fn unknown_type_is_rejected_not_silently_dropped() {
    let mut bogus = fixture("editor", "ready");
    bogus["type"] = serde_json::json!("madeUpType");
    assert!(matches!(
        parse_editor_message_value(bogus),
        Err(ProtocolError::UnknownType(_))
    ));

    let mut bogus_host = fixture("host", "markSaved");
    bogus_host["type"] = serde_json::json!("noSuchHostMessage");
    assert!(matches!(
        parse_host_message_value(bogus_host),
        Err(ProtocolError::UnknownType(_))
    ));
}

#[test]
fn version_mismatch_is_rejected() {
    let mut stale = fixture("editor", "ready");
    stale["protocolVersion"] = serde_json::json!(2);
    assert!(matches!(
        parse_editor_message_value(stale),
        Err(ProtocolError::VersionMismatch { got: 2 })
    ));
}

#[test]
fn oversize_message_is_rejected() {
    let mut big = fixture("editor", "error");
    big["payload"] = serde_json::json!({ "blob": "x".repeat(17 * 1024 * 1024) });
    assert!(matches!(
        parse_editor_message_value(big),
        Err(ProtocolError::TooLarge)
    ));
}

#[test]
fn malformed_json_is_rejected() {
    assert!(matches!(
        parse_editor_message_value(serde_json::json!({"type": 42})),
        Err(ProtocolError::Malformed(_))
    ));
}

#[test]
fn type_enums_expose_all_names() {
    // 同步测试(protocol_sync.rs)负责与 protocol.ts 比对;此处锁定枚举数量
    // 防止意外收缩。protocol.ts 为准:宿主 12,编辑器 32。
    assert_eq!(HostMessageType::all_names().len(), 12);
    assert_eq!(EditorMessageType::all_names().len(), 32);
}
