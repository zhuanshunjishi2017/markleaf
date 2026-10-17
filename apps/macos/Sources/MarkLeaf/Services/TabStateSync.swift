import Foundation

/// 标签轻量状态同步的纯映射（可独立测试）。
enum TabStateSync {
    static func apply(
        tab: DocumentTab,
        fileName: String?,
        isDirty: Bool,
        isReadOnly: Bool = false,
        isReadOnlyLocked: Bool? = nil,
        hasPendingExternalChange: Bool = false,
        scrollTop: Double? = nil,
        readingAnchor: ReadingAnchor? = nil,
        revision: Int64,
        encoding: String,
        newLine: String,
        visualSelectionFrom: Int? = nil,
        visualSelectionTo: Int? = nil,
        sourceSelectionFrom: Int? = nil,
        sourceSelectionTo: Int? = nil,
        untitledLabel: String
    ) {
        tab.isDirty = isDirty
        tab.isReadOnly = isReadOnly
        tab.isReadOnlyLocked = isReadOnlyLocked ?? isReadOnly
        tab.hasPendingExternalChange = hasPendingExternalChange
        if let scrollTop { tab.scrollTop = scrollTop }
        if let readingAnchor { tab.readingAnchor = readingAnchor }
        tab.contentRevision = revision
        tab.encoding = encoding
        tab.newLine = newLine
        tab.visualSelectionFrom = visualSelectionFrom
        tab.visualSelectionTo = visualSelectionTo
        tab.sourceSelectionFrom = sourceSelectionFrom
        tab.sourceSelectionTo = sourceSelectionTo
        if let fileName {
            tab.path = fileName
            tab.fileIdentity = FileIdentityPolicy.identity(forPath: fileName)
            tab.title = URL(fileURLWithPath: fileName).lastPathComponent
        } else {
            tab.path = nil
            tab.fileIdentity = nil
            tab.title = tab.untitledSequence.map { "\(untitledLabel) \($0)" } ?? untitledLabel
        }
    }
}
