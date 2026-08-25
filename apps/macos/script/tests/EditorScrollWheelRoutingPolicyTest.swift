import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(EditorScrollWheelRoutingPolicy.route(command: false, control: false) == .scroll,
       "ordinary wheel input should stay on WebKit's native scrolling path")
expect(EditorScrollWheelRoutingPolicy.route(command: true, control: false) == .commandZoom,
       "Command-wheel input should be consumed by native zoom routing")
expect(EditorScrollWheelRoutingPolicy.route(command: false, control: true) == .pinchZoom,
       "Control-wheel input emitted by a trackpad pinch should use native pinch zoom")
expect(EditorScrollWheelRoutingPolicy.route(command: true, control: true) == .commandZoom,
       "Command-wheel should keep precedence when both modifiers are present")

print("PASS")
