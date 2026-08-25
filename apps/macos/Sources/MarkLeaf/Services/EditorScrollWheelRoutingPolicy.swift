enum EditorScrollWheelRoute: Equatable {
    case scroll
    case commandZoom
    case pinchZoom
}

enum EditorScrollWheelRoutingPolicy {
    static func route(command: Bool, control: Bool) -> EditorScrollWheelRoute {
        if command {
            return .commandZoom
        }
        if control {
            return .pinchZoom
        }
        return .scroll
    }
}
