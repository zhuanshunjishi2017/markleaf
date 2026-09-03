import Foundation

enum MultiTabModePolicy {
    static func showsTabBar(isEnabled: Bool) -> Bool {
        isEnabled
    }

    static func allowsTabCreation(isEnabled: Bool) -> Bool {
        isEnabled
    }

    static func workspacePrefersNewTab(
        workspacePreference: Bool,
        multiTabEnabled: Bool
    ) -> Bool {
        workspacePreference && multiTabEnabled
    }

    static func externalFileMode(
        _ mode: ExternalFileOpenMode,
        multiTabEnabled: Bool
    ) -> ExternalFileOpenMode {
        guard !multiTabEnabled, mode == .newTab else { return mode }
        return .currentWindow
    }
}
