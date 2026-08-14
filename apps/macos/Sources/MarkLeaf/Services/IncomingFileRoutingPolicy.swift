enum IncomingFileRouteAction: Equatable {
    case activateExisting
    case replaceActive
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
        if mode == .currentWindow && eventIndex == 0 && hasActiveEditor { return .replaceActive }
        return .createWindow
    }
}
