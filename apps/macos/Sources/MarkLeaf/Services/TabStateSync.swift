import Foundation

/// 标签轻量状态同步的纯映射（可独立测试）。
enum TabStateSync {
    static func apply(
        tab: DocumentTab,
        fileName: String?,
        isDirty: Bool,
        revision: Int64,
        encoding: String,
        newLine: String,
        untitledLabel: String
    ) {
        tab.isDirty = isDirty
        tab.contentRevision = revision
        tab.encoding = encoding
        tab.newLine = newLine
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
