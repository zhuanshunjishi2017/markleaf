import Foundation

/// 打开文件的纯决策：去重命中则激活，未命中则建模新标签（真实加载由窗口层执行）。
enum TabOpenResolution {
    enum Result {
        case activateExisting(DocumentTabID)
        case created(DocumentTab)
    }

    static func resolve(store: TabStore, url: URL, untitledLabel: String) -> Result {
        let identity = FileIdentityPolicy.identity(for: url)
        if let existing = store.tab(withIdentity: identity) {
            return .activateExisting(existing.tabID)
        }
        // 阶段 2 起由会话实际编码覆盖；此处保持单一默认值避免引入额外依赖。
        let tab = DocumentTab(
            path: url.path,
            title: url.lastPathComponent,
            encoding: "UTF-8",
            newLine: "LF"
        )
        store.append(tab)
        return .created(tab)
    }
}
