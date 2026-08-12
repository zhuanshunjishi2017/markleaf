# Sidebar Localization and Responsive Header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the English sidebar's Chinese initial empty state and replace the truncating header text action with a compact native icon plus a complete empty-state action.

**Architecture:** Keep localization canonical in `L10n`, make `SidebarView` construct and refresh all copy through one injected localization function, and route both folder controls to the existing folder picker. Put the sidebar width constants and clamp in a small pure `SidebarLayout` type so window restoration, animation, divider constraints, and tests share the same 200-point minimum.

**Tech Stack:** Swift 5.9, AppKit, SF Symbols, Swift Package Manager, XCTest, macOS 13+

## Global Constraints

- Keep the default workspace width at exactly 230 points.
- Keep visible sidebar widths at or above exactly 200 points.
- Reserve at least exactly 420 points for the editor.
- Use the system symbol `folder.badge.plus`; add no custom icon asset.
- Use native AppKit controls and the existing folder picker action.
- Add no custom animation, language-specific width constant, or new user preference.
- The header icon is visible only on the Workspace tab and remains available after a workspace opens.
- The empty-state text button uses its natural width.

---

## File Map

- Modify `macos/Sources/MarkLeaf/Services/L10n.swift`: replace the obsolete multiline sidebar key with the standalone status key in English, Traditional Chinese, and Japanese.
- Modify `macos/Sources/MarkLeaf/Views/SidebarView.swift`: build the adaptive header and empty state, localize them at construction and refresh time, and share the existing action.
- Modify `macos/Sources/MarkLeaf/App/AppDelegate.swift`: keep the `--sidebar-test` diagnostic working by finding the empty-state view through its stable identifier instead of Chinese copy.
- Create `macos/Sources/MarkLeaf/Views/SidebarLayout.swift`: own the 200-point sidebar minimum and 420-point editor reserve, plus saved-width clamping.
- Modify `macos/Sources/MarkLeaf/Views/EditorWindowController.swift`: consume `SidebarLayout` in every restore, animation, persistence, and split-view constraint path.
- Modify `macos/Tests/MarkLeafTests/L10nJapaneseTests.swift`: lock translations and table completeness for the new standalone key.
- Create `macos/Tests/MarkLeafTests/SidebarViewTests.swift`: cover construction-time localization, language refresh, icon layout, shared action, and empty-state visibility.
- Create `macos/Tests/MarkLeafTests/SidebarLayoutTests.swift`: cover saved-width clamping and constants without constructing a web view or window controller.

---

### Task 1: Canonical Standalone Empty-State Localization

**Files:**
- Modify: `macos/Tests/MarkLeafTests/L10nJapaneseTests.swift:5-17`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift:169-170,526-527,882-883`

**Interfaces:**
- Consumes: `L10n.translate(_:language:) -> String` and `L10n.translationKeys(for:) -> Set<String>`.
- Produces: the canonical key `暂未打开工作区` in `ja`, `zh-Hant`, and `en`; removes the unused multiline key `暂未打开工作区\n点击“打开文件夹”开始`.

- [ ] **Step 1: Write the failing localization tests**

Add these assertions to `testJapaneseSpotTranslations()` and add the key-replacement test:

```swift
XCTAssertEqual(L10n.translate("暂未打开工作区", language: "en"), "No workspace open")
XCTAssertEqual(L10n.translate("暂未打开工作区", language: "zh-Hant"), "尚未開啟工作區")
XCTAssertEqual(L10n.translate("暂未打开工作区", language: "ja"), "ワークスペースはまだ開かれていません")

func testSidebarUsesStandaloneEmptyStateKey() {
    let obsolete = "暂未打开工作区\n点击“打开文件夹”开始"
    XCTAssertFalse(L10n.translationKeys(for: "en").contains(obsolete))
    XCTAssertFalse(L10n.translationKeys(for: "zh-Hant").contains(obsolete))
    XCTAssertFalse(L10n.translationKeys(for: "ja").contains(obsolete))
}
```

- [ ] **Step 2: Run the focused test and verify failure**

Run:

```bash
swift test --package-path macos --filter L10nJapaneseTests
```

Expected: FAIL because `暂未打开工作区` falls back to Simplified Chinese and every table still contains the obsolete multiline key.

- [ ] **Step 3: Replace the key in all three translation tables**

Make these exact replacements in `L10n.swift`:

```swift
// Japanese
"暂未打开工作区": "ワークスペースはまだ開かれていません",

// Traditional Chinese
"暂未打开工作区": "尚未開啟工作區",

// English
"暂未打开工作区": "No workspace open",
```

Remove the three old multiline entries. Because one key is replaced by one key in each table, keep the Japanese completeness count at `343`.

- [ ] **Step 4: Run the focused test and verify success**

Run:

```bash
swift test --package-path macos --filter L10nJapaneseTests
```

Expected: PASS, including the existing `ja.count == 343` assertion.

- [ ] **Step 5: Commit the localization change**

```bash
git add macos/Sources/MarkLeaf/Services/L10n.swift macos/Tests/MarkLeafTests/L10nJapaneseTests.swift
git commit -m "fix(macos): localize sidebar empty state"
```

---

### Task 2: Adaptive Sidebar Header and Empty State

**Files:**
- Create: `macos/Tests/MarkLeafTests/SidebarViewTests.swift`
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift:4-172`
- Modify: `macos/Sources/MarkLeaf/App/AppDelegate.swift:594-618`

**Interfaces:**
- Consumes: `L10n.t(_:)`, `L10n.translate(_:language:)`, `EditorSession.workspaceRoot`, and the existing `SidebarView.openFolder()` selector.
- Produces: `SidebarView.init(session:localize:)`, `SidebarView.emptyStateIdentifier`, `updateEmptyStateVisibility(hasWorkspace:)`, and package-internal view references used by XCTest.

- [ ] **Step 1: Write construction, refresh, action, layout, and visibility tests**

Create `SidebarViewTests.swift` with the following tests. Retain the window and sidebar for the duration of layout tests so AppKit has a live view hierarchy.

```swift
import AppKit
import XCTest
@testable import MarkLeaf

final class SidebarViewTests: XCTestCase {
    @MainActor
    func testEnglishCopyIsLocalizedAtConstruction() {
        let sidebar = makeSidebar(language: "en")

        XCTAssertEqual(sidebar.tabControl.label(forSegment: 0), "Workspace")
        XCTAssertEqual(sidebar.tabControl.label(forSegment: 1), "Outline")
        XCTAssertEqual(sidebar.emptyStateLabel.stringValue, "No workspace open")
        XCTAssertEqual(sidebar.emptyStateOpenFolderButton.title, "Open Folder")
        XCTAssertFalse(sidebar.emptyStateLabel.stringValue.contains("暂未"))
    }

    @MainActor
    func testApplyLanguageRefreshesEverySidebarControl() {
        var language = "en"
        let sidebar = SidebarView(session: EditorSession()) {
            L10n.translate($0, language: language)
        }

        language = "ja"
        sidebar.applyLanguage()

        XCTAssertEqual(sidebar.tabControl.label(forSegment: 0), "ワークスペース")
        XCTAssertEqual(sidebar.tabControl.label(forSegment: 1), "アウトライン")
        XCTAssertEqual(sidebar.headerOpenFolderButton.toolTip, "フォルダを開く")
        XCTAssertEqual(sidebar.headerOpenFolderButton.accessibilityLabel(), "フォルダを開く")
        XCTAssertEqual(sidebar.emptyStateLabel.stringValue, "ワークスペースはまだ開かれていません")
        XCTAssertEqual(sidebar.emptyStateOpenFolderButton.title, "フォルダを開く")
    }

    @MainActor
    func testFolderControlsShareOneAction() {
        let sidebar = makeSidebar(language: "en")

        XCTAssertTrue(sidebar.headerOpenFolderButton.target === sidebar)
        XCTAssertTrue(sidebar.emptyStateOpenFolderButton.target === sidebar)
        XCTAssertEqual(sidebar.headerOpenFolderButton.action, sidebar.emptyStateOpenFolderButton.action)
    }

    @MainActor
    func testEnglishHeaderFitsAtDefaultWidth() throws {
        let sidebar = makeSidebar(language: "en")
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 230, height: 600),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = sidebar
        sidebar.frame = NSRect(x: 0, y: 0, width: 230, height: 600)
        sidebar.layoutSubtreeIfNeeded()

        let header = try XCTUnwrap(sidebar.headerOpenFolderButton.superview as? NSStackView)
        XCTAssertEqual(sidebar.headerOpenFolderButton.title, "")
        XCTAssertNotNil(sidebar.headerOpenFolderButton.image)
        XCTAssertEqual(sidebar.headerOpenFolderButton.imagePosition, .imageOnly)
        XCTAssertEqual(sidebar.headerOpenFolderButton.frame.width, 32, accuracy: 0.5)
        XCTAssertGreaterThanOrEqual(
            sidebar.headerOpenFolderButton.frame.minX,
            sidebar.tabControl.frame.maxX + header.spacing - 0.5
        )
        XCTAssertGreaterThanOrEqual(
            sidebar.emptyStateOpenFolderButton.frame.width,
            sidebar.emptyStateOpenFolderButton.intrinsicContentSize.width - 0.5
        )
    }

    @MainActor
    func testEmptyStateFollowsTabAndWorkspaceAvailability() {
        let sidebar = makeSidebar(language: "en")

        XCTAssertFalse(sidebar.emptyStateView.isHidden)
        sidebar.selectTab(1)
        XCTAssertTrue(sidebar.emptyStateView.isHidden)
        sidebar.selectTab(0)
        sidebar.updateEmptyStateVisibility(hasWorkspace: true)
        XCTAssertTrue(sidebar.emptyStateView.isHidden)
        XCTAssertFalse(sidebar.headerOpenFolderButton.isHidden)
        sidebar.updateEmptyStateVisibility(hasWorkspace: false)
        XCTAssertFalse(sidebar.emptyStateView.isHidden)
    }

    @MainActor
    private func makeSidebar(language: String) -> SidebarView {
        SidebarView(session: EditorSession()) {
            L10n.translate($0, language: language)
        }
    }
}
```

- [ ] **Step 2: Run the new tests and verify failure**

Run:

```bash
swift test --package-path macos --filter SidebarViewTests
```

Expected: compilation FAIL because `init(session:localize:)`, the new controls, and `updateEmptyStateVisibility(hasWorkspace:)` do not exist yet.

- [ ] **Step 3: Define the localized controls and stable empty-state identity**

In `SidebarView`, replace the current private controls with package-internal read-only references for deterministic UI tests, and store the production-default localization function:

```swift
static let emptyStateIdentifier = NSUserInterfaceItemIdentifier("Sidebar.emptyState")

let tabControl: NSSegmentedControl
let headerOpenFolderButton = NSButton()
let emptyStateLabel: NSTextField
let emptyStateOpenFolderButton: NSButton
let emptyStateView: NSStackView

private let localize: (String) -> String

init(
    session: EditorSession,
    localize: @escaping (String) -> String = L10n.t
) {
    self.session = session
    self.localize = localize
    self.tabControl = NSSegmentedControl(
        labels: [localize("工作区"), localize("大纲")],
        trackingMode: .selectOne,
        target: nil,
        action: nil
    )
    self.emptyStateLabel = NSTextField(labelWithString: localize("暂未打开工作区"))
    self.emptyStateOpenFolderButton = NSButton(
        title: localize("打开文件夹"),
        target: nil,
        action: nil
    )
    self.emptyStateView = NSStackView()
    super.init(frame: .zero)
}
```

Keep `containerView`, trees, and scroll views private. The initializer's default is exactly `L10n.t`, so production follows the live app setting; tests inject `L10n.translate` without mutating or writing the user's settings.

- [ ] **Step 4: Build the icon header and natural-width empty state**

Configure the header control with the SF Symbol and a fixed 32-point width:

```swift
headerOpenFolderButton.image = NSImage(
    systemSymbolName: "folder.badge.plus",
    accessibilityDescription: nil
)
headerOpenFolderButton.imagePosition = .imageOnly
headerOpenFolderButton.title = ""
headerOpenFolderButton.bezelStyle = .rounded
headerOpenFolderButton.controlSize = .regular
headerOpenFolderButton.target = self
headerOpenFolderButton.action = #selector(openFolder)
headerOpenFolderButton.translatesAutoresizingMaskIntoConstraints = false
headerOpenFolderButton.widthAnchor.constraint(equalToConstant: 32).isActive = true
```

Use the same selector for the empty-state button, and configure the vertical stack without a width constraint on the button:

```swift
emptyStateLabel.alignment = .center
emptyStateLabel.textColor = .secondaryLabelColor
emptyStateLabel.font = .systemFont(ofSize: 12)

emptyStateOpenFolderButton.bezelStyle = .rounded
emptyStateOpenFolderButton.controlSize = .regular
emptyStateOpenFolderButton.target = self
emptyStateOpenFolderButton.action = #selector(openFolder)

emptyStateView.orientation = .vertical
emptyStateView.alignment = .centerX
emptyStateView.spacing = 10
emptyStateView.identifier = Self.emptyStateIdentifier
emptyStateView.addArrangedSubview(emptyStateLabel)
emptyStateView.addArrangedSubview(emptyStateOpenFolderButton)
emptyStateView.translatesAutoresizingMaskIntoConstraints = false
```

Put `headerOpenFolderButton` in the existing header stack, replace `placeholder` with `emptyStateView` in the view hierarchy, and keep the existing center-X/center-Y constraints pointed at `emptyStateView`.

- [ ] **Step 5: Centralize sidebar copy refresh and empty-state visibility**

Replace `applyLanguage()` with:

```swift
func applyLanguage() {
    tabControl.setLabel(localize("工作区"), forSegment: 0)
    tabControl.setLabel(localize("大纲"), forSegment: 1)
    let openFolderTitle = localize("打开文件夹")
    headerOpenFolderButton.toolTip = openFolderTitle
    headerOpenFolderButton.setAccessibilityLabel(openFolderTitle)
    emptyStateLabel.stringValue = localize("暂未打开工作区")
    emptyStateOpenFolderButton.title = openFolderTitle
}
```

Call `applyLanguage()` once during initialization after the controls are configured. Add the shared visibility method and use it from both `showTab(_:)` and `workspaceChanged()`:

```swift
func updateEmptyStateVisibility(hasWorkspace: Bool) {
    emptyStateView.isHidden = !(session.sidebarTabIndex == 0 && !hasWorkspace)
}

private func workspaceChanged() {
    updateEmptyStateVisibility(hasWorkspace: session.workspaceRoot != nil)
    workspaceTree.reloadData()
}
```

In `showTab(_:)`, set `headerOpenFolderButton.isHidden = !workspaceActive` and call `updateEmptyStateVisibility(hasWorkspace: session.workspaceRoot != nil)`. Do not change the existing Workspace/Outline crossfade animation.

- [ ] **Step 6: Update the built-in sidebar diagnostic**

In `AppDelegate.swift`, replace the Chinese-copy search in `placeholderVisible(in:)` with the stable view identifier:

```swift
func placeholderVisible(in view: NSView) -> Bool? {
    if view.identifier == SidebarView.emptyStateIdentifier {
        return !view.isHidden
    }
    for subview in view.subviews {
        if let found = placeholderVisible(in: subview) {
            return found
        }
    }
    return nil
}
```

This keeps `--sidebar-test` language-independent.

- [ ] **Step 7: Run the focused tests and verify success**

Run:

```bash
swift test --package-path macos --filter SidebarViewTests
```

Expected: all five tests PASS.

- [ ] **Step 8: Commit the adaptive sidebar UI**

```bash
git add macos/Sources/MarkLeaf/Views/SidebarView.swift macos/Sources/MarkLeaf/App/AppDelegate.swift macos/Tests/MarkLeafTests/SidebarViewTests.swift
git commit -m "fix(macos): adapt sidebar folder controls"
```

---

### Task 3: Shared Sidebar Width Policy

**Files:**
- Create: `macos/Sources/MarkLeaf/Views/SidebarLayout.swift`
- Create: `macos/Tests/MarkLeafTests/SidebarLayoutTests.swift`
- Modify: `macos/Sources/MarkLeaf/Views/EditorWindowController.swift:49-59,156-159,214-229,263-285`

**Interfaces:**
- Consumes: saved `AppSettings.workspaceWidth: Int` and `NSSplitViewDelegate` coordinates.
- Produces: `SidebarLayout.minimumWidth`, `SidebarLayout.minimumEditorWidth`, and `SidebarLayout.clampedWorkspaceWidth(_:) -> CGFloat`.

- [ ] **Step 1: Write the failing pure layout tests**

Create `SidebarLayoutTests.swift`:

```swift
import XCTest
@testable import MarkLeaf

final class SidebarLayoutTests: XCTestCase {
    func testSavedWidthBelowMinimumClampsToTwoHundred() {
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(150), 200)
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(199), 200)
    }

    func testValidSavedWidthsArePreserved() {
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(200), 200)
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(230), 230)
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(320), 320)
    }

    func testEditorReserveRemainsFourHundredTwenty() {
        XCTAssertEqual(SidebarLayout.minimumEditorWidth, 420)
    }
}
```

- [ ] **Step 2: Run the focused test and verify failure**

Run:

```bash
swift test --package-path macos --filter SidebarLayoutTests
```

Expected: compilation FAIL because `SidebarLayout` does not exist.

- [ ] **Step 3: Add the pure layout policy**

Create `SidebarLayout.swift`:

```swift
import CoreGraphics

enum SidebarLayout {
    static let minimumWidth: CGFloat = 200
    static let minimumEditorWidth: CGFloat = 420

    static func clampedWorkspaceWidth(_ savedWidth: Int) -> CGFloat {
        max(CGFloat(savedWidth), minimumWidth)
    }
}
```

Do not add a `defaultWidth` here: `AppSettings.workspaceWidth` remains the single owner of the existing 230-point default.

- [ ] **Step 4: Replace every visible-width literal in the window controller**

Use the shared clamp when restoring and sizing:

```swift
let saved = SidebarLayout.clampedWorkspaceWidth(
    SettingsService.shared.settings.workspaceWidth
)
splitView.setPosition(saved, ofDividerAt: 0)
```

Use the same clamped value in `buildContent()` for the initial window content width and `workspaceDividerPosition`, and in `applyViewState()` as the animation target.

Replace the persistence and delegate literals with:

```swift
if width >= SidebarLayout.minimumWidth {
    SettingsService.shared.update { $0.workspaceWidth = Int(width) }
}

if isAnimatingSidebar { return 0 }
return max(proposedMinimumPosition, SidebarLayout.minimumWidth)

min(
    proposedMaximumPosition,
    splitView.bounds.width - SidebarLayout.minimumEditorWidth
)
```

The animation may still reach zero while hiding; the 200-point rule applies only while visible.

- [ ] **Step 5: Run layout and settings tests**

Run:

```bash
swift test --package-path macos --filter SidebarLayoutTests
swift test --package-path macos --filter SettingsRigidityTests
```

Expected: both suites PASS; `AppSettings.workspaceWidth` still defaults to `230`.

- [ ] **Step 6: Commit the width policy**

```bash
git add macos/Sources/MarkLeaf/Views/SidebarLayout.swift macos/Sources/MarkLeaf/Views/EditorWindowController.swift macos/Tests/MarkLeafTests/SidebarLayoutTests.swift
git commit -m "fix(macos): enforce usable sidebar width"
```

---

### Task 4: Full Regression and Real-App Acceptance

**Files:**
- Verify: all files changed in Tasks 1-3
- Verify: `macos/script/build_and_run.sh`

**Interfaces:**
- Consumes: the completed localized adaptive sidebar and width policy.
- Produces: test, build, and direct UI evidence against every acceptance criterion.

- [ ] **Step 1: Run the complete test suite**

Run:

```bash
swift test --package-path macos
```

Expected: all tests PASS with zero failures.

- [ ] **Step 2: Build, package, launch, and verify the actual app process**

Run from `macos/`:

```bash
./script/build_and_run.sh --verify
```

Expected: the script prepares resources, builds the Swift package, creates `macos/dist/MarkLeaf.app`, launches it, and prints `[verify] 进程存在`.

- [ ] **Step 3: Verify English first render at 230 points**

In the running app, select English and relaunch so construction-time behavior is exercised. Confirm:

- `Workspace` and `Outline` are fully visible.
- The trailing control is the native `folder.badge.plus` icon, not `Open F...`.
- The center reads `No workspace open` and shows a complete `Open Folder` button.
- No Simplified Chinese empty-state text is visible.
- The icon's Tooltip reads `Open Folder`.

- [ ] **Step 4: Verify resize, tab, and workspace behavior**

Confirm directly in the running app:

- Dragging the divider stops at 200 points while the sidebar is visible.
- Workspace/Outline tab switching retains the existing system crossfade.
- The header folder icon disappears on Outline and reappears on Workspace.
- Opening a folder hides the empty-state stack but leaves the header icon available.
- Both folder controls present the same native directory picker.
- Relaunching with a previously saved width below 200 restores the visible sidebar at 200.

- [ ] **Step 5: Verify language refresh and appearance**

Switch English to Japanese and Traditional Chinese without rebuilding. Confirm the segmented labels, header Tooltip/accessibility label, status label, and empty-state button all update. Check light and dark appearances for clipping, overlap, or custom-looking controls.

- [ ] **Step 6: Inspect the final diff and repository state**

Run:

```bash
git diff --check
git status --short
git log -4 --oneline
```

Expected: `git diff --check` prints nothing; only intentional files are changed; the three implementation commits follow the design and plan commits.
