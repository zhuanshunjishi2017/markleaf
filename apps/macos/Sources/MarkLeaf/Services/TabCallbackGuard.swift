import Foundation

/// 异步回调凭据：携带标签与发起时的代次。
struct AsyncCallbackToken: Equatable {
    let tabID: DocumentTabID
    let generation: Int
}

/// 代次守卫：切换标签、重新加载文档、恢复重建时递增代次；
/// 旧代次的回调一律丢弃，保证过期异步结果不覆盖当前文档。
final class TabCallbackGuard {
    private var generations: [DocumentTabID: Int] = [:]

    func token(for tabID: DocumentTabID) -> AsyncCallbackToken {
        if generations[tabID] == nil {
            generations[tabID] = 0
        }
        return AsyncCallbackToken(tabID: tabID, generation: generations[tabID] ?? 0)
    }

    func invalidate(_ tabID: DocumentTabID) {
        generations[tabID, default: 0] += 1
    }

    func remove(_ tabID: DocumentTabID) {
        generations.removeValue(forKey: tabID)
    }

    func shouldApply(_ token: AsyncCallbackToken) -> Bool {
        guard let current = generations[token.tabID] else { return false }
        return current == token.generation
    }
}
