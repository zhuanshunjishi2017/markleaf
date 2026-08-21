import AppKit

private var failures = 0

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        failures += 1
        fputs("FAIL: \(message)\n", stderr)
        return
    }
}

let compact = PreferencesWindowLayout.windowContentSize(
    for: NSSize(width: 420, height: 500)
)
expect(compact.width == PreferencesWindowLayout.minimumWindowWidth,
       "preference window should use a compact width floor")
expect(compact.height <= PreferencesWindowLayout.maximumWindowHeight,
       "preference window height should stay within the compact ceiling")

let wide = PreferencesWindowLayout.windowContentSize(
    for: NSSize(width: 720, height: 700)
)
expect(wide.width == PreferencesWindowLayout.maximumWindowWidth,
       "preference window should cap unusually wide tab content")
expect(wide.height == PreferencesWindowLayout.maximumWindowHeight,
       "preference window should cap unusually tall tab content")

let column = PreferencesWindowLayout.centeredColumnFrame(
    containerWidth: compact.width,
    fittingWidth: 320
)
let expectedMargin = (compact.width - column.width) / 2
expect(abs(column.minX - expectedMargin) < 0.5,
       "settings content column should be centered in the window")
expect(column.width <= PreferencesWindowLayout.maximumContentColumnWidth,
       "settings content column should have a readable maximum width")

expect(PreferencesWindowLayout.bottomBarTopInset >= 10,
       "bottom action bar should keep enough top breathing room")
expect(PreferencesWindowLayout.bottomBarBottomInset == 12,
       "bottom action bar should use a balanced bottom inset")
expect(PreferencesWindowLayout.filePageContentHorizontalOffset < 0,
       "file page content should be nudged left to correct its visual center")

if failures > 0 {
    exit(1)
}
print("PASS")
