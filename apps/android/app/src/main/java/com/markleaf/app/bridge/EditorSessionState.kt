package com.markleaf.app.bridge

/**
 * 编辑器会话守卫状态：documentId 匹配 + revision 单调推进 + 请求-响应关联。
 * 语义对齐 apps/windows/MarkLeaf/Editor/EditorSession.cs，含一处修正：
 * startDocument 重置 revision（C# 端不重置导致新文档消息被当作过期消息吞掉，
 * 见 hp-pro 日志诊断记录）。
 */
class EditorSessionState {

    var documentId: String = ""
        private set

    var confirmedRevision: Long = 0
        private set

    private val pendingRequests = LinkedHashMap<String, String>()

    /** 开始新文档：切换 documentId 并重置 revision 基线。 */
    fun startDocument(id: String, revision: Long = 0) {
        documentId = id
        confirmedRevision = maxOf(0L, revision)
        pendingRequests.clear()
    }

    /**
     * 编辑器消息准入判定：文档归属 + revision 不回退。
     * 拒绝时调用方应记日志（不静默丢弃）。
     * 例外：ready 豁免——前端 ready 携带自生成占位 documentId（此时双方
     * 尚未约定会话 id），宿主靠它开启会话（对齐 C# allowReadyWithoutDocument）。
     */
    fun accept(message: EditorMessage): Boolean {
        if (message is EditorMessage.Ready) {
            return true
        }
        if (message.documentId != documentId) {
            return false
        }
        if (message.revision < confirmedRevision) {
            return false
        }
        confirmedRevision = maxOf(confirmedRevision, message.revision)
        return true
    }

    /** 注册待回包请求：生成或采用调用方指定的 id。 */
    fun registerRequest(responseType: String, requestId: String? = null): String {
        val id = requestId ?: newRequestId()
        pendingRequests[id] = responseType
        return id
    }

    /** 请求回包核销：id 存在且响应类型一致时成功；完成后不可重复核销。 */
    fun completeRequest(requestId: String?, responseType: String): Boolean {
        if (requestId.isNullOrEmpty()) {
            return false
        }
        val expected = pendingRequests[requestId] ?: return false
        if (expected != responseType) {
            return false
        }
        pendingRequests.remove(requestId)
        return true
    }

    private fun newRequestId(): String = "req-${System.nanoTime().toString(36)}"
}
