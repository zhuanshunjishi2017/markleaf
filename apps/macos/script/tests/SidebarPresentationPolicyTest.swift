import Foundation

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

print("PASS")
