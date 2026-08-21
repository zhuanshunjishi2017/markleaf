import Foundation

struct StatusBarDisplayModel: Equatable {
    var commandStatus: String
    var blockType: String
    var line: Int
    var column: Int
    var characterCount: Int
    var encoding: String
    var newLine: String
    var mode: String
    var zoomPercent: Int
}

enum StatusBarDisplayPolicy {
    /// The dedicated zoom field already carries this feedback, so avoid rendering it twice.
    static func shouldShowCommandStatus(
        commandStatus: String,
        zoomVisible: Bool,
        zoomStatus: String
    ) -> Bool {
        !zoomVisible || commandStatus != zoomStatus
    }
}
