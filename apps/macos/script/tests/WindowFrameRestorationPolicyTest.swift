import Foundation
import CoreGraphics

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
let inside = CGRect(x: 100, y: 80, width: 1100, height: 760)
expect(WindowFrameRestorationPolicy.constrain(inside, screens: [screen]) == inside, "onscreen frame stays untouched")
let offscreen = CGRect(x: 5000, y: 5000, width: 1100, height: 760)
let clamped = WindowFrameRestorationPolicy.constrain(offscreen, screens: [screen])
expect(!screen.intersection(clamped).isNull && clamped.size.width == 1100 && clamped.size.height == 760, "offscreen frame returns onscreen with size")
expect(WindowFrameRestorationPolicy.constrain(offscreen, screens: []).equalTo(offscreen), "no screens leaves frame unchanged")
let same = CGRect(x: 200, y: 200, width: 1100, height: 760)
let staggered = WindowFrameRestorationPolicy.stagger([same, same, same])
expect(staggered[0].equalTo(same) && staggered[1].origin != same.origin && staggered[2].origin != staggered[1].origin, "duplicates stagger without reordering")
expect(WindowFrameRestorationPolicy.stagger([same, CGRect(x: 300, y: 200, width: 1100, height: 760)])[1].origin.x == 300, "distinct frames are untouched")
print("PASS")
