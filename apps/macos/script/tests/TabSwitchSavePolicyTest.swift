import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(TabSwitchSavePolicy.action(isDirty: false, hasPath: true, autoSaveOnSwitch: true) == .nothing, "clean tabs switch without any save work")
expect(TabSwitchSavePolicy.action(isDirty: true, hasPath: true, autoSaveOnSwitch: true) == .save, "dirty pathed tabs save when enabled")
expect(TabSwitchSavePolicy.action(isDirty: true, hasPath: true, autoSaveOnSwitch: false) == .nothing, "dirty pathed tabs keep buffer when disabled")
expect(TabSwitchSavePolicy.action(isDirty: true, hasPath: false, autoSaveOnSwitch: true) == .snapshotOnly, "untitled dirty tabs snapshot immediately")
expect(TabSwitchSavePolicy.action(isDirty: false, hasPath: false, autoSaveOnSwitch: true) == .nothing, "clean untitled tabs switch without work")

print("PASS")
