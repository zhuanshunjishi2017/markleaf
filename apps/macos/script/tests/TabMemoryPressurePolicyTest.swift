import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func tab(_ name: String, dirty: Bool, activated: Date) -> DocumentTab {
    let t = DocumentTab(path: "/tmp/\(name).md", title: "\(name).md", encoding: "UTF-8", newLine: "LF")
    t.isDirty = dirty
    t.lastActivatedAt = activated
    return t
}

let now = Date()
let cleanOld = tab("cleanOld", dirty: false, activated: now.addingTimeInterval(-300))
let cleanNew = tab("cleanNew", dirty: false, activated: now.addingTimeInterval(-30))
let dirtyOld = tab("dirtyOld", dirty: true, activated: now.addingTimeInterval(-200))
let active = tab("active", dirty: true, activated: now)
let suspended = tab("suspended", dirty: false, activated: now.addingTimeInterval(-500))
suspended.isSuspended = true

let order = TabMemoryPressurePolicy.suspensionOrder(
    tabs: [cleanOld, cleanNew, dirtyOld, active, suspended],
    activeTabID: active.tabID
)
expect(order == [cleanOld.tabID, cleanNew.tabID, dirtyOld.tabID],
       "suspend clean LRU first, dirty last, never the active or already-suspended tabs")

expect(
    TabMemoryPressurePolicy.suspensionOrder(tabs: [active], activeTabID: active.tabID).isEmpty,
    "only the active tab leaves nothing to suspend"
)
print("PASS")
