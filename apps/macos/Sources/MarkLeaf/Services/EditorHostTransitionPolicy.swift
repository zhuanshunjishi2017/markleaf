import Foundation

enum EditorHostTransitionPolicy {
    static func shouldAnimate(
        from previousTabID: String?,
        to targetTabID: String,
        requested: Bool,
        reduceMotion: Bool
    ) -> Bool {
        requested && !reduceMotion && previousTabID != nil && previousTabID != targetTabID
    }
}
