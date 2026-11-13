// EditorBridge:protocol.ts 宿主侧会话逻辑——ready 握手前的消息缓存、
// stale 门控转发、宿主→编辑器发送。
// GTK 交互经 HostToEditorSink 抽象(WebKitSink 用 evaluate 注入),
// 纯逻辑(队列/门控)可无头单测(tests/editor_bridge_logic.rs)。
use std::cell::RefCell;
use std::collections::VecDeque;
use std::rc::Rc;

use super::messages::{EditorMessage, HostMessage};
use super::session_state::EditorSessionState;

/// 宿主→编辑器的发送通道抽象。
pub trait HostToEditorSink {
    fn send(&self, message: &HostMessage);
}

/// WebKit 实现:经 EditorWebView 的 evaluate 注入(主循环线程)。
pub struct WebKitSink(pub crate::editor::webview::EditorWebView);

impl HostToEditorSink for WebKitSink {
    fn send(&self, message: &HostMessage) {
        self.0.post_to_editor(message);
    }
}

pub struct EditorBridge<S: HostToEditorSink> {
    sink: Rc<S>,
    session: Rc<RefCell<EditorSessionState>>,
    ready_received: bool,
    pending: VecDeque<HostMessage>,
    on_message: Box<dyn Fn(&EditorMessage)>,
}

impl<S: HostToEditorSink> EditorBridge<S> {
    pub fn new(
        sink: Rc<S>,
        session: Rc<RefCell<EditorSessionState>>,
        on_message: impl Fn(&EditorMessage) + 'static,
    ) -> Self {
        Self {
            sink,
            session,
            ready_received: false,
            pending: VecDeque::new(),
            on_message: Box::new(on_message),
        }
    }

    /// 宿主→编辑器:ready 前缓存,ready 后直发(Android EditorBridge 同款语义)。
    pub fn post(&mut self, message: &HostMessage) {
        if self.ready_received {
            self.sink.send(message);
        } else {
            self.pending.push_back(message.clone());
        }
    }

    /// 编辑器→宿主入口:会话门控 + ready 握手 + 向上转发。
    pub fn handle(&mut self, message: EditorMessage) {
        if message.message_type == crate::bridge::messages::EditorMessageType::Ready {
            self.ready_received = true;
            self.flush();
            (self.on_message)(&message);
            return;
        }
        let decision = self.session.borrow().accept(&message);
        match decision {
            super::session_state::AcceptDecision::Accept => {
                // documentLoaded/snapshot 等推进已确认 revision。
                self.session.borrow_mut().confirm(&message);
                (self.on_message)(&message);
            }
            other => {
                eprintln!(
                    "[bridge] 拒绝消息 type={} doc={} rev={}: {other:?}",
                    message.message_type_name(),
                    message.document_id,
                    message.revision
                );
            }
        }
    }

    /// 切换文档:重置门控(见 session_state 修正项)。
    pub fn start_document(&mut self, document_id: &str) {
        self.session.borrow_mut().start_document(document_id);
    }

    fn flush(&mut self) {
        while let Some(message) = self.pending.pop_front() {
            self.sink.send(&message);
        }
    }
}
