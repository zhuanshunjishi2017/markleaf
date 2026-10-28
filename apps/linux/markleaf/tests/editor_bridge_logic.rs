// EditorBridge 纯逻辑测试(pending 队列/会话门控/命令转发)。
// GTK 交互(scheme/evaluate 注入)由冒烟覆盖;此处用 RecordingSink 无头验证。
use std::cell::RefCell;
use std::rc::Rc;

use markleaf::bridge::editor_bridge::{EditorBridge, HostToEditorSink};
use markleaf::bridge::messages::EditorMessageType;
use markleaf::bridge::protocol::{EditorEnvelope, HostEnvelope};
use markleaf::bridge::session_state::EditorSessionState;

#[derive(Default)]
struct RecordingSink {
    sent: RefCell<Vec<HostEnvelope>>,
}

impl HostToEditorSink for RecordingSink {
    fn send(&self, message: &HostEnvelope) {
        self.sent.borrow_mut().push(message.clone());
    }
}

fn editor_message(type_: EditorMessageType, doc: &str, revision: i64) -> EditorEnvelope {
    EditorEnvelope {
        protocol_version: 1,
        message_type: type_,
        document_id: doc.to_string(),
        revision,
        request_id: None,
        payload: None,
    }
}

fn host_message(command: &str, doc: &str, revision: i64) -> HostEnvelope {
    HostEnvelope::command(doc, revision, serde_json::json!({ "command": command }))
}

#[test]
fn buffers_host_messages_until_ready_then_flushes_in_order() {
    let sink = Rc::new(RecordingSink::default());
    let session = Rc::new(RefCell::new(EditorSessionState::new()));
    let forwarded = Rc::new(RefCell::new(Vec::new()));
    let forwarded_cb = forwarded.clone();
    let mut bridge = EditorBridge::new(sink.clone(), session, move |msg| {
        forwarded_cb.borrow_mut().push(msg.message_type)
    });

    // ready 前发送:全部缓存。
    bridge.post(&host_message("undo", "doc-1", 0));
    bridge.post(&host_message("redo", "doc-1", 0));
    assert_eq!(sink.sent.borrow().len(), 0, "ready 前不得直发");

    // ready 到达:缓存依序 flush。
    bridge.handle(editor_message(EditorMessageType::Ready, "", 0));
    let sent = sink.sent.borrow();
    assert_eq!(sent.len(), 2);
    assert_eq!(sent[0].payload.as_ref().unwrap()["command"], "undo");
    assert_eq!(sent[1].payload.as_ref().unwrap()["command"], "redo");
    drop(sent);

    // ready 后直发。
    bridge.post(&host_message("insertText", "doc-1", 0));
    assert_eq!(sink.sent.borrow().len(), 3);
    // ready 本身转发给宿主回调。
    assert_eq!(*forwarded.borrow(), vec![EditorMessageType::Ready]);
}

#[test]
fn stale_editor_messages_are_filtered_by_session_gate() {
    let sink = Rc::new(RecordingSink::default());
    let session = Rc::new(RefCell::new(EditorSessionState::new()));
    let mut bridge = EditorBridge::new(sink.clone(), session, |_| {});

    bridge.handle(editor_message(EditorMessageType::Ready, "", 0));
    bridge.handle(editor_message(EditorMessageType::DocumentLoaded, "doc-1", 3));
    // revision 回退:拒绝且不转发。
    let stale = editor_message(EditorMessageType::Snapshot, "doc-1", 2);
    bridge.handle(stale);

    let forwarded = Rc::new(RefCell::new(Vec::new()));
    let forwarded_cb = forwarded.clone();
    let session2 = Rc::new(RefCell::new(EditorSessionState::new()));
    let mut bridge2 = EditorBridge::new(sink.clone(), session2, move |msg| {
        forwarded_cb.borrow_mut().push(msg.message_type)
    });
    bridge2.handle(editor_message(EditorMessageType::Ready, "", 0));
    bridge2.start_document("doc-1");
    bridge2.handle(editor_message(EditorMessageType::DocumentLoaded, "doc-1", 3));
    bridge2.handle(editor_message(EditorMessageType::Snapshot, "doc-1", 4));
    assert_eq!(*forwarded.borrow(), vec![EditorMessageType::Ready, EditorMessageType::DocumentLoaded, EditorMessageType::Snapshot]);
}

#[test]
fn document_switch_resets_gate_via_start_document() {
    let sink = Rc::new(RecordingSink::default());
    let session = Rc::new(RefCell::new(EditorSessionState::new()));
    let mut bridge = EditorBridge::new(sink.clone(), session, |_| {});
    bridge.handle(editor_message(EditorMessageType::Ready, "", 0));
    bridge.handle(editor_message(EditorMessageType::DocumentLoaded, "doc-1", 9));
    // 切换文档后,新文档 revision 0 的消息必须被接受(修正 Windows bug 的语义)。
    bridge.start_document("doc-2");
    bridge.handle(editor_message(EditorMessageType::Snapshot, "doc-2", 0));
}
