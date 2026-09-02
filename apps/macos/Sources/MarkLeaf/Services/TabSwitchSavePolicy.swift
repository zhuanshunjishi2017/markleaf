import Foundation

enum TabSwitchSavePolicy {
    enum Action: Equatable { case nothing, save, snapshotOnly }

    static func action(isDirty: Bool, hasPath: Bool, autoSaveOnSwitch: Bool) -> Action {
        guard isDirty else { return .nothing }
        guard hasPath else { return .snapshotOnly }
        return autoSaveOnSwitch ? .save : .nothing
    }
}
