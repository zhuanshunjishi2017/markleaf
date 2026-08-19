enum ApplicationReopenAction: Equatable {
    case none
    case showExistingWindow
    case createNewWindow
}

enum ApplicationLifecyclePolicy {
    static let terminateAfterLastWindowClosed = false

    static func reopenAction(
        hasVisibleWindows: Bool,
        hasEditorWindow: Bool
    ) -> ApplicationReopenAction {
        if hasVisibleWindows {
            return .none
        }
        return hasEditorWindow ? .showExistingWindow : .createNewWindow
    }
}
