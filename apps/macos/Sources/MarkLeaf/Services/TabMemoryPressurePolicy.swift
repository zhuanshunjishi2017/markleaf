import Foundation

/// 内存压力下的标签暂停顺序：干净标签按最久未激活优先，脏标签最后；
/// 活动标签与已暂停标签不参与。调用方在暂停前必须先完成恢复快照。
enum TabMemoryPressurePolicy {
    static func suspensionOrder(tabs: [DocumentTab], activeTabID: DocumentTabID?) -> [DocumentTabID] {
        let candidates = tabs.filter { $0.tabID != activeTabID && !$0.isSuspended }
        let clean = candidates
            .filter { !$0.isDirty }
            .sorted { $0.lastActivatedAt < $1.lastActivatedAt }
        let dirty = candidates
            .filter(\.isDirty)
            .sorted { $0.lastActivatedAt < $1.lastActivatedAt }
        return (clean + dirty).map(\.tabID)
    }
}
