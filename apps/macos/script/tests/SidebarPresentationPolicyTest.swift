import Foundation
import CoreGraphics

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(SidebarPresentationPolicy.shouldAnimateReveal(wasArranged: false, visibilityChanged: true),
       "revealing a sidebar that was not arranged at startup should animate after normalizing its start width")
expect(SidebarPresentationPolicy.shouldAnimateReveal(wasArranged: true, visibilityChanged: true),
       "an arranged sidebar should animate when visibility changes")
expect(!SidebarPresentationPolicy.shouldAnimateReveal(wasArranged: true, visibilityChanged: false),
       "unchanged sidebar visibility should not animate")

// 左侧栏隐藏时，属于左栏内容的工作区/大纲/树结构/文档列表应置灰；
// “在右侧显示大纲”控制独立右栏，因此不受左栏可见性影响。
expect(SidebarMenuPolicy.leftSidebarContentEnabled(sidebarVisible: true),
       "left sidebar content stays enabled while the sidebar is visible")
expect(!SidebarMenuPolicy.leftSidebarContentEnabled(sidebarVisible: false),
       "left sidebar content is greyed out when the sidebar is hidden")

expect(
    SidebarPresentationPolicy.hiddenLayoutAction(
        isArranged: true,
        visibilityChanged: false,
        currentWidth: 0
    ) == .keepHiddenWithoutDividerMutation,
    "opening or saving a document must not mutate the divider when the sidebar remains hidden"
)
expect(
    SidebarPresentationPolicy.hiddenLayoutAction(
        isArranged: true,
        visibilityChanged: true,
        currentWidth: 240
    ) == .animateCollapse,
    "an explicit visible-to-hidden transition should still animate"
)
expect(
    SidebarPresentationPolicy.hiddenLayoutAction(
        isArranged: false,
        visibilityChanged: false,
        currentWidth: 0
    ) == .keepHiddenWithoutDividerMutation,
    "a sidebar detached at startup should remain detached on state refresh"
)

expect(
    SidebarPresentationPolicy.minimumDividerCoordinate(
        isSidebarVisible: false,
        isAnimating: false,
        minimumWidth: 200
    ) == 0,
    "a hidden sidebar must be allowed to collapse to zero so resizing the window does not reopen it"
)
expect(
    SidebarPresentationPolicy.minimumDividerCoordinate(
        isSidebarVisible: true,
        isAnimating: false,
        minimumWidth: 200
    ) == 200,
    "a visible sidebar should keep its normal minimum width"
)
expect(
    SidebarPresentationPolicy.minimumDividerCoordinate(
        isSidebarVisible: true,
        isAnimating: true,
        minimumWidth: 200
    ) == 0,
    "an animating sidebar should be allowed to collapse to zero"
)

let sliderStart = CGRect(x: 0, y: 0, width: 82, height: 28)
let sliderEnd = CGRect(x: 84, y: 0, width: 62, height: 28)
let sliderHalfway = SidebarTabTransitionPolicy.interpolatedFrame(
    from: sliderStart,
    to: sliderEnd,
    progress: 0.5
)
expect(sliderHalfway == CGRect(x: 42, y: 0, width: 72, height: 28),
       "the sidebar tab slider should move and resize together")

let reversedHalfway = SidebarTabTransitionPolicy.interpolatedFrame(
    from: sliderHalfway,
    to: sliderStart,
    progress: 0.5
)
expect(reversedHalfway == CGRect(x: 21, y: 0, width: 77, height: 28),
       "a reversed transition should continue from the currently displayed slider frame")
expect(
    !SidebarTabTransitionPolicy.shouldApplyCompletion(transition: 1, currentTransition: 2),
    "completion from an interrupted tab transition must not hide the newly selected view"
)
expect(
    SidebarTabTransitionPolicy.shouldApplyCompletion(transition: 2, currentTransition: 2),
    "the latest tab transition should finalize its selected view"
)

print("PASS")
