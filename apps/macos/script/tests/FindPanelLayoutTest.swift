import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(FindPanelLayout.contentHeight(isReplaceExpanded: false) == 74,
       "collapsed panel should use one find row plus options")
expect(FindPanelLayout.contentHeight(isReplaceExpanded: true) == 110,
       "expanded panel should reveal the replace row")
expect(FindPanelLayout.replaceRowHeight(isExpanded: false) == 0,
       "collapsed replacement row should animate down to zero height")
expect(FindPanelLayout.replaceRowHeight(isExpanded: true) == 28,
       "expanded replacement row should use its full control height")
expect(FindPanelLayout.replaceTopSpacing(isExpanded: false) == 0,
       "collapsed replacement row should not leave an extra stack gap")
expect(FindPanelLayout.replaceTopSpacing(isExpanded: true) == 8,
       "expanded replacement row should restore the standard row gap")

let original = NSRect(x: 100, y: 300, width: 444, height: 96)
let expanded = FindPanelLayout.frameKeepingTop(
    currentFrame: original,
    targetHeight: 132
)
expect(expanded.maxY == original.maxY, "expanding should keep the panel's top edge fixed")
expect(expanded.minY == 264, "expanding should grow downward")

print("PASS")
