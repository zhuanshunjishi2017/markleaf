import Foundation

enum TabExternalChangePolicy {
    enum Action: Equatable { case ignore, reloadPreservingPosition, presentConflict, keepPending, showMissing }
    static func action(isDirty: Bool, fileExists: Bool, fingerprintChanged: Bool) -> Action {
        guard fileExists else { return isDirty ? .keepPending : .showMissing }
        guard fingerprintChanged else { return .ignore }
        return isDirty ? .presentConflict : .reloadPreservingPosition
    }
}
