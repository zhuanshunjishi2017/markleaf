import Foundation

enum DocumentWindowTitle {
    static func format(
        fileName: String?,
        isDirty: Bool,
        untitledLabel: String,
        modifiedLabel: String
    ) -> String {
        let baseName = fileName?.isEmpty == false ? fileName! : untitledLabel
        return isDirty ? "\(baseName) - \(modifiedLabel)" : baseName
    }
}
