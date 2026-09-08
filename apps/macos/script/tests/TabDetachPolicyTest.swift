import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let frame = NSRect(x: 100, y: 100, width: 400, height: 300)
expect(TabDetachPolicy.action(globalPoint: NSPoint(x: 300, y: 250), windowFrame: frame) == .reorder, "an interior drag should reorder")
expect(TabDetachPolicy.action(globalPoint: NSPoint(x: 95, y: 250), windowFrame: frame) == .reorder, "just outside the window should tolerate an accidental release")
expect(TabDetachPolicy.action(globalPoint: NSPoint(x: 70, y: 250), windowFrame: frame) == .detach, "a drag beyond the tolerance should detach")
expect(TabDetachPolicy.action(globalPoint: NSPoint(x: 60, y: 250), windowFrame: frame) == .detach, "a drag beyond the left edge should detach")
expect(TabDetachPolicy.action(globalPoint: NSPoint(x: 300, y: 430), windowFrame: frame) == .detach, "a drag above the window should detach")
print("PASS")
