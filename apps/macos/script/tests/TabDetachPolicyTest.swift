import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

// 窗口 100,100 起点尺寸 400x300；标签栏条带贴在窗口顶部右侧（左侧留给侧边栏），高 32。
let windowFrame = NSRect(x: 100, y: 100, width: 400, height: 300)
let strip = NSRect(x: 330, y: 368, width: 170, height: 32)

func action(_ point: NSPoint) -> TabDetachPolicy.Action {
    TabDetachPolicy.action(globalPoint: point, stripGlobalRect: strip, windowFrame: windowFrame)
}

// 条带内左右移动始终是重排。
expect(action(NSPoint(x: 400, y: 384)) == .reorder, "dragging inside the strip should reorder")
expect(action(NSPoint(x: 250, y: 384)) == .reorder, "dragging to the left of the strip inside the window should still reorder")

// 轻微下移仍算重排，避免手抖就拆窗。
expect(action(NSPoint(x: 400, y: 380)) == .reorder, "a small vertical wobble should still reorder")
expect(action(NSPoint(x: 400, y: 355)) == .reorder, "a drop just below the strip should still reorder")

// 向下拖进编辑区 → 撕下（本次新增的关键行为）。
expect(action(NSPoint(x: 400, y: 320)) == .detach, "dragging down into the content should tear off")
expect(action(NSPoint(x: 400, y: 150)) == .detach, "dragging far down inside the window should tear off")

// 向上越过标题栏 → 撕下。
expect(action(NSPoint(x: 400, y: 430)) == .detach, "dragging above the window should tear off")

// 左右拖出窗口 → 撕下。
expect(action(NSPoint(x: 70, y: 384)) == .detach, "dragging beyond the left edge should tear off")
expect(action(NSPoint(x: 540, y: 384)) == .detach, "dragging beyond the right edge should tear off")

// 吸附区用于跨窗口命中测试：条带外扩一个撕下距离。
let region = TabDetachPolicy.stripRegion(stripGlobalRect: strip)
expect(region.contains(NSPoint(x: 400, y: 355)), "the hit region covers just below the strip")
expect(!region.contains(NSPoint(x: 400, y: 300)), "the hit region stops at the tear-off distance")

// 释放去向：落在别的窗口标签栏上优先并入，其次撕下，最后重排。
func outcome(_ point: NSPoint, hit: Bool) -> TabDetachPolicy.DropOutcome {
    TabDetachPolicy.outcome(hasOtherStripHit: hit, globalPoint: point, stripGlobalRect: strip, windowFrame: windowFrame)
}
expect(outcome(NSPoint(x: 400, y: 384), hit: true) == .transfer, "a hit on another strip transfers the tab")
expect(outcome(NSPoint(x: 400, y: 384), hit: false) == .reorder, "a drop on the source strip reorders")
expect(outcome(NSPoint(x: 400, y: 300), hit: false) == .tearOff, "a drop below the strip tears off")
expect(outcome(NSPoint(x: 400, y: 300), hit: true) == .transfer, "another strip wins over tearing off")

// 撕下窗口原点：光标在窗口内的相对位置保持不变。
let origin = TabDetachPolicy.tearOffOrigin(
    globalPoint: NSPoint(x: 800, y: 600),
    windowPoint: NSPoint(x: 300, y: 260)
)
expect(origin == NSPoint(x: 500, y: 340), "the torn-off window keeps the cursor at the same relative spot")
print("PASS")
