// 会话纪律测试:revision 门控(文档切换必须重置,修正 Windows 端
// ConfirmedRevision 不重置的已知 bug)、requestId 配对、类型匹配。
use markleaf::bridge::messages::{parse_editor_message_value, EditorMessageType};
use markleaf::bridge::protocol::EditorEnvelope;
use markleaf::bridge::session_state::{AcceptDecision, EditorSessionState};

fn envelope(type_: EditorMessageType, doc: &str, revision: i64, request_id: Option<&str>) -> EditorEnvelope {
    EditorEnvelope {
        protocol_version: 1,
        message_type: type_,
        document_id: doc.to_string(),
        revision,
        request_id: request_id.map(str::to_string),
        payload: None,
    }
}

fn parse_json_type(name: &str) -> EditorMessageType {
    // 从枚举名反查:直接取同名字段的 serde rename 后字符串比对不可行,
    // 这里以 fixtures 的解析路径构造真实消息。
    let path = format!(
        "{}/tests/fixtures/protocol/editor_{}.json",
        env!("CARGO_MANIFEST_DIR"),
        name
    );
    let text = std::fs::read_to_string(&path).expect("fixture 存在");
    parse_editor_message_value(serde_json::from_str(&text).unwrap())
        .expect("fixture 可解析")
        .message_type
}

#[test]
fn rejects_stale_revision_and_wrong_document() {
    let mut state = EditorSessionState::new();
    state.start_document("doc-1");
    state.confirm_revision(5);

    assert_eq!(
        state.accept(&envelope(EditorMessageType::Snapshot, "doc-1", 4, None)),
        AcceptDecision::RejectStale
    );
    assert_eq!(
        state.accept(&envelope(EditorMessageType::Snapshot, "doc-1", 5, None)),
        AcceptDecision::Accept
    );
    assert_eq!(
        state.accept(&envelope(EditorMessageType::Snapshot, "doc-2", 9, None)),
        AcceptDecision::RejectWrongDocument
    );
}

#[test]
fn revision_resets_on_new_document() {
    let mut state = EditorSessionState::new();
    state.start_document("doc-1");
    state.confirm_revision(9);
    // 新文档:revision 归零,旧文档的低 revision 消息在正确 documentId 下接受。
    state.start_document("doc-2");
    assert_eq!(
        state.accept(&envelope(EditorMessageType::Snapshot, "doc-2", 0, None)),
        AcceptDecision::Accept
    );
    assert_eq!(
        state.accept(&envelope(EditorMessageType::Snapshot, "doc-2", 1, None)),
        AcceptDecision::Accept
    );
}

#[test]
fn request_id_pairs_with_expected_response_type() {
    let mut state = EditorSessionState::new();
    state.start_document("doc-1");
    let id = state.register_request(EditorMessageType::Snapshot);
    // 同类型响应完成配对。
    assert!(state.complete_request(&id, EditorMessageType::Snapshot));
    // 重复完成失败。
    assert!(!state.complete_request(&id, EditorMessageType::Snapshot));
    // 类型不匹配失败。
    let id2 = state.register_request(EditorMessageType::StylesApplied);
    assert!(!state.complete_request(&id2, EditorMessageType::Snapshot));
}

#[test]
fn confirm_uses_revision_from_message() {
    let mut state = EditorSessionState::new();
    state.start_document("doc-1");
    let msg = envelope(EditorMessageType::DocumentLoaded, "doc-1", 7, None);
    state.confirm(&msg);
    assert_eq!(
        state.accept(&envelope(EditorMessageType::Snapshot, "doc-1", 6, None)),
        AcceptDecision::RejectStale
    );
    let _ = parse_json_type("documentLoaded"); // 保持 fixture 解析路径被引用
}

/// 文档切换竞态(对齐 Android ac280dab 的回归测试):
/// 旧文档迟到消息先把基线推到 N,新文档的 documentLoaded(rev=0) 回执必须
/// 无条件接受并把基线重置为 0;否则新文档全部后续消息被拒,编辑器冻结。
#[test]
fn document_loaded_resyncs_baseline_past_stale_inflight_revisions() {
    let mut state = EditorSessionState::new();
    state.start_document("doc-1");
    // 旧文档消息推进基线到 5(在宿主 start_document 重置之后迟到到达)。
    let stale = envelope(EditorMessageType::DirtyChanged, "doc-1", 5, None);
    let _ = state.accept(&stale);
    state.confirm(&stale);
    // 新文档回执 rev=0:接受 + 基线重置。
    let receipt = envelope(EditorMessageType::DocumentLoaded, "doc-1", 0, None);
    assert_eq!(state.accept(&receipt), AcceptDecision::Accept);
    state.confirm(&receipt);
    // 回执之后的正常新消息(rev=1 < 旧基线 5)应被接受。
    assert_eq!(
        state.accept(&envelope(EditorMessageType::DirtyChanged, "doc-1", 1, None)),
        AcceptDecision::Accept
    );
}
