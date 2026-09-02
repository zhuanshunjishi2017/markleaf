enum IncomingFileRouteAction: Equatable {
    case activateExisting
    case replaceActive
    case newTabInActiveWindow
    case createWindow
}

enum IncomingFileRoutingPolicy {
    static func action(
        mode: ExternalFileOpenMode,
        eventIndex: Int,
        hasActiveEditor: Bool,
        hasOpenDuplicate: Bool
    ) -> IncomingFileRouteAction {
        if hasOpenDuplicate { return .activateExisting }
        switch mode {
        case .newWindow:
            return .createWindow
        case .newTab:
            return hasActiveEditor ? .newTabInActiveWindow : .createWindow
        case .currentWindow:
            if eventIndex == 0, hasActiveEditor { return .replaceActive }
            return .createWindow
        }
    }
}
