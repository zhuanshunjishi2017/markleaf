import Foundation

/// 标签拖出新窗口时携带的完整文档状态。
struct DetachedTabDocument {
    let markdown: String
    let fileURL: URL?
    let title: String
    let encoding: String
    let newLine: String
    let isDirty: Bool
    let isReadOnly: Bool
    let untitledSequence: Int?
    let documentKind: NewDocumentKind
    let selection: PendingDocumentSelection?
}
