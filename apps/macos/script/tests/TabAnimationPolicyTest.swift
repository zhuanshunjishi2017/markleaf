import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func inRange(_ value: TimeInterval, _ range: ClosedRange<TimeInterval>) -> Bool {
    range.contains(value)
}

expect(inRange(TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: false), 0.16...0.22),
       "insert/remove/reorder duration must be 160-220 ms")
expect(inRange(TabAnimationPolicy.duration(for: .activeState, reduceMotion: false), 0.12...0.16),
       "active state duration must be 120-160 ms")
expect(inRange(TabAnimationPolicy.duration(for: .statusMark, reduceMotion: false), 0.10...0.14),
       "status mark duration must be 100-140 ms")
expect(TabAnimationPolicy.duration(for: .editorFade, reduceMotion: false) <= 0.10,
       "editor fade must not exceed ~100 ms")

expect(TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: true) == 0,
       "reduce motion collapses durations to zero")
expect(!TabAnimationPolicy.allowsMotion(reduceMotion: true),
       "reduce motion disables displacement animations")
expect(TabAnimationPolicy.allowsMotion(reduceMotion: false),
       "normal mode allows motion")
print("PASS")
