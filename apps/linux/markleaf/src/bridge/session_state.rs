// 会话纪律:documentId + revision 门控与 requestId 配对。
// 语义对齐 Windows EditorSession.Accept / Android EditorSessionState,
// 含已知 bug 修正:文档切换必须重置 confirmed revision
// (Windows 端不重置导致新文档消息被吞,见 spec §4.2)。
use super::protocol::{EditorEnvelope, EditorMessageType};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AcceptDecision {
    Accept,
    RejectStale,
    RejectWrongDocument,
}

#[derive(Debug, Default)]
pub struct EditorSessionState {
    document_id: String,
    confirmed_revision: i64,
    pending_requests: std::collections::HashMap<String, EditorMessageType>,
}

impl EditorSessionState {
    pub fn new() -> Self {
        Self::default()
    }

    /// 开始新文档:重置 documentId 与 confirmed revision(修正项)。
    pub fn start_document(&mut self, document_id: &str) {
        self.document_id = document_id.to_string();
        self.confirmed_revision = 0;
        self.pending_requests.clear();
    }

    /// 直接确认 revision(如宿主本地推进)。
    pub fn confirm_revision(&mut self, revision: i64) {
        if revision > self.confirmed_revision {
            self.confirmed_revision = revision;
        }
    }

    /// 由编辑器消息确认 revision(documentLoaded/stylesApplied/snapshot 类)。
    pub fn confirm(&mut self, message: &EditorEnvelope) {
        if message.document_id == self.document_id {
            self.confirm_revision(message.revision);
        }
    }

    /// 门控:仅接受当前文档且 revision 未回退的消息。拒绝时由调用方记日志。
    pub fn accept(&self, message: &EditorEnvelope) -> AcceptDecision {
        if message.document_id != self.document_id {
            return AcceptDecision::RejectWrongDocument;
        }
        if message.revision < self.confirmed_revision {
            return AcceptDecision::RejectStale;
        }
        AcceptDecision::Accept
    }

    /// 注册一个待完成请求,返回 requestId。
    pub fn register_request(&mut self, response_type: EditorMessageType) -> String {
        let id = format!("req-{}", uuid_v4());
        self.pending_requests.insert(id.clone(), response_type);
        id
    }

    /// 完成请求:存在且响应类型匹配时返回 true(并移除)。
    pub fn complete_request(&mut self, request_id: &str, response_type: EditorMessageType) -> bool {
        match self.pending_requests.remove(request_id) {
            Some(expected) => expected == response_type,
            None => false,
        }
    }

    pub fn document_id(&self) -> &str {
        &self.document_id
    }
}

// 轻量唯一 id(glib/gtk 无关;协议 requestId 无格式要求,唯一即可)。
fn uuid_v4() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    // 进程内单调计数保证同纳秒冲突也不重复。
    use std::sync::atomic::{AtomicU64, Ordering};
    static COUNTER: AtomicU64 = AtomicU64::new(0);
    let counter = COUNTER.fetch_add(1, Ordering::Relaxed);
    format!("{nanos:016x}{counter:08x}")
}
