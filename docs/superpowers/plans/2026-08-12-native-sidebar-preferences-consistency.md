# Native Sidebar and Preferences Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix recovery-window localization, make Outline visually and behaviorally match Workspace, and preserve Preferences context during language changes.

**Architecture:** Keep Workspace and Outline as separate native outline views while sharing one presentation definition. Separate outline-content events from active-selection events so data reloads never erase native selection, and preserve Preferences page/frame through the existing controller rebuild. Keep recovery copy in a small pure formatter used by the real AppKit controller.

**Tech Stack:** Swift 5.9, AppKit, WKWebView protocol messages, Swift Package Manager, XCTest, macOS 13+

## Global Constraints

- Support exactly Simplified Chinese, Traditional Chinese, English, and Japanese; add no fifth language.
- Simplified Chinese recovery copy must omit `（上次异常退出遗留）`.
- Use natural singular and plural English recovery sentences.
- Workspace and Outline must use a 13-point regular system font and 26-point rows.
- Preserve Outline indentation; do not use semibold text for level-one or level-two headings.
- Keep native non-emphasized source-list selection; add no custom painted selection background.
- Reload Outline only when heading content changes, never solely because the active heading changes.
- Preserve Preferences page and frame only across an in-session language rebuild; do not persist the page across launches.
- Do not modify Windows source or shared EditorWeb behavior.

---

## File Map

- Modify `macos/Sources/MarkLeaf/Services/L10n.swift`: add pure language-specific formatting and replace the obsolete recovery message key with singular/plural keys.
- Modify `macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift`: consume localized singular/plural recovery copy and expose the actual introductory label to package tests.
- Create `macos/Tests/MarkLeafTests/RecoveryWindowLocalizationTests.swift`: verify four-language copy and the real AppKit controller output.
- Modify `macos/Sources/MarkLeaf/Views/SidebarView.swift`: share tree presentation, route outline activation after selection, and synchronize active rows without reload feedback.
- Modify `macos/Sources/MarkLeaf/Services/EditorSession.swift`: split outline-content and active-selection callbacks and propagate a null active position.
- Modify `macos/Tests/MarkLeafTests/OutlineWorkspaceStyleTests.swift`: compare real Workspace/Outline typography and selection geometry.
- Create `macos/Tests/MarkLeafTests/OutlineSelectionInteractionTests.swift`: exercise real AppKit first-click selection and programmatic synchronization.
- Modify `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`: retain the tab controller and restore a clamped initial page.
- Modify `macos/Sources/MarkLeaf/App/AppWindowManager.swift`: capture and restore visible Preferences context during language rebuild.
- Create `macos/Tests/MarkLeafTests/PreferencesLanguageRefreshTests.swift`: verify page clamping and visible/hidden restoration policy.

---

### Task 1: Four-Language Recovery Introduction

**Files:**
- Create: `macos/Tests/MarkLeafTests/RecoveryWindowLocalizationTests.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift:19-26, 270-282, 646-658, 1008-1024`
- Modify: `macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift:4-30`

**Interfaces:**
- Produces: `L10n.format(_:language:arguments:) -> String`.
- Produces: `RecoveryWindowCopy.introduction(snapshotCount:language:) -> String`.
- Produces: `RecoveryWindowController.introductionLabel: NSTextField` and `init(snapshots:language:)` with the language defaulting to current settings.

- [ ] **Step 1: Write failing copy and controller tests**

Create `RecoveryWindowLocalizationTests.swift`:

```swift
import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class RecoveryWindowLocalizationTests: XCTestCase {
    func testRecoveryIntroductionHasNaturalSingularCopyInAllLanguages() {
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 1, language: "zh-Hans"),
                       "检测到 1 个未保存的文档。请选择要恢复的快照：")
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 1, language: "zh-Hant"),
                       "偵測到 1 個未儲存的文件。請選擇要復原的快照：")
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 1, language: "en"),
                       "Found 1 unsaved document. Choose a snapshot to recover:")
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 1, language: "ja"),
                       "1 件の未保存ドキュメントが見つかりました。復元するスナップショットを選択してください：")
    }

    func testRecoveryIntroductionHasNaturalPluralCopyInAllLanguages() {
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 2, language: "zh-Hans"),
                       "检测到 2 个未保存的文档。请选择要恢复的快照：")
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 2, language: "zh-Hant"),
                       "偵測到 2 個未儲存的文件。請選擇要復原的快照：")
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 2, language: "en"),
                       "Found 2 unsaved documents. Choose a snapshot to recover:")
        XCTAssertEqual(RecoveryWindowCopy.introduction(snapshotCount: 2, language: "ja"),
                       "2 件の未保存ドキュメントが見つかりました。復元するスナップショットを選択してください：")
    }

    func testEnglishRecoveryWindowUsesEnglishIntroduction() {
        let snapshot = RecoverySnapshot(
            documentId: "probe", documentPath: nil, markdown: "# Probe",
            revision: 1, timestamp: Date(timeIntervalSince1970: 0), displayName: "Probe"
        )
        let controller = RecoveryWindowController(snapshots: [snapshot], language: "en")

        XCTAssertEqual(controller.window?.title, "Recover Unsaved Documents")
        XCTAssertEqual(controller.introductionLabel.stringValue,
                       "Found 1 unsaved document. Choose a snapshot to recover:")
        XCTAssertFalse(controller.introductionLabel.stringValue.contains("异常退出"))
    }

    func testObsoleteParentheticalRecoveryKeyIsRemoved() {
        let obsolete = "检测到 %d 个未保存的文档（上次异常退出遗留）。选择要恢复的快照："
        for language in ["en", "zh-Hant", "ja"] {
            XCTAssertFalse(L10n.translationKeys(for: language).contains(obsolete))
        }
    }
}
```

- [ ] **Step 2: Run the recovery localization tests and verify RED**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-recovery-red/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-recovery-red/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-recovery-red/scratch \
  --filter RecoveryWindowLocalizationTests
```

Expected: compilation fails because `RecoveryWindowCopy`, the language-specific initializer, and `introductionLabel` do not exist. This is the expected missing-behavior failure.

- [ ] **Step 3: Add pure language-specific formatting and recovery copy**

In `L10n.swift`, add:

```swift
static func format(_ format: String, language: String, arguments: [CVarArg]) -> String {
    String(format: translate(format, language: language), arguments: arguments)
}
```

Replace the obsolete recovery key in all three translation tables with these two canonical keys and their exact translations from the tests:

```swift
"检测到 1 个未保存的文档。请选择要恢复的快照："
"检测到 %d 个未保存的文档。请选择要恢复的快照："
```

In `RecoveryWindowController.swift`, add:

```swift
enum RecoveryWindowCopy {
    static let singular = "检测到 1 个未保存的文档。请选择要恢复的快照："
    static let plural = "检测到 %d 个未保存的文档。请选择要恢复的快照："

    static func introduction(snapshotCount: Int, language: String) -> String {
        if snapshotCount == 1 {
            return L10n.translate(singular, language: language)
        }
        return L10n.format(plural, language: language, arguments: [snapshotCount])
    }
}
```

Change the controller initializer and label construction:

```swift
let introductionLabel: NSTextField

init(
    snapshots: [RecoverySnapshot],
    language: String = SettingsService.shared.settings.displayLanguage
) {
    self.snapshots = snapshots
    introductionLabel = NSTextField(labelWithString:
        RecoveryWindowCopy.introduction(snapshotCount: snapshots.count, language: language)
    )
    // Use L10n.translate(..., language:) for the title, columns, and buttons in this initializer.
}
```

Use `introductionLabel` in constraints instead of the old local hard-coded `label`.

- [ ] **Step 4: Run focused localization tests and verify GREEN**

Run the Step 2 command again.

Expected: `RecoveryWindowLocalizationTests` passes with 4 tests and no failures.

- [ ] **Step 5: Run localization completeness tests**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-recovery-red/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-recovery-red/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-recovery-red/scratch \
  --filter L10nJapaneseTests
```

Expected: PASS after updating the exact Japanese key-count assertion to account for replacing one obsolete key with two new keys.

- [ ] **Step 6: Commit the recovery localization fix**

```bash
git add macos/Sources/MarkLeaf/Services/L10n.swift \
  macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift \
  macos/Tests/MarkLeafTests/L10nJapaneseTests.swift \
  macos/Tests/MarkLeafTests/RecoveryWindowLocalizationTests.swift
git commit -m "fix(macos): localize recovery window message"
```

---

### Task 2: Shared Workspace and Outline Presentation

**Files:**
- Modify: `macos/Tests/MarkLeafTests/OutlineWorkspaceStyleTests.swift`
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift:238-275, 560-620`

**Interfaces:**
- Produces: `SidebarTreePresentation.apply(to:)` and `SidebarTreePresentation.rowFont`.
- Consumed by: `WorkspaceTreeView.configure(session:)` and `OutlineTreeView.configure(session:onHeadingActivated:)`.

- [ ] **Step 1: Replace the weak style assertion with a real parity test**

Update `OutlineWorkspaceStyleTests.swift` so it creates both real cells and rows:

```swift
@MainActor
func testOutlineAndWorkspaceUseIdenticalNativeRowPresentation() throws {
    let workspace = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
    workspace.configure(session: EditorSession())
    let outline = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
    outline.configure(session: EditorSession())
    let file = WorkspaceEntry(name: "file.md", path: "/probe/file.md", isDirectory: false)
    let heading = OutlineHeading(level: 1, text: "Heading", position: 0)

    let workspaceCell = try XCTUnwrap(workspace.outlineView(workspace, viewFor: nil, item: file) as? NSTableCellView)
    let outlineCell = try XCTUnwrap(outline.outlineView(outline, viewFor: nil, item: heading) as? NSTableCellView)
    let workspaceRow = try XCTUnwrap(workspace.outlineView(workspace, rowViewForItem: file))
    let outlineRow = try XCTUnwrap(outline.outlineView(outline, rowViewForItem: heading))

    XCTAssertEqual(outline.rowSizeStyle, workspace.rowSizeStyle)
    XCTAssertEqual(outline.rowHeight, workspace.rowHeight)
    XCTAssertEqual(outline.intercellSpacing, workspace.intercellSpacing)
    XCTAssertEqual(outline.selectionHighlightStyle, workspace.selectionHighlightStyle)
    XCTAssertEqual(outlineCell.textField?.font?.pointSize, workspaceCell.textField?.font?.pointSize)
    XCTAssertFalse(NSFontManager.shared.traits(of: try XCTUnwrap(outlineCell.textField?.font)).contains(.boldFontMask))
    XCTAssertTrue(workspaceRow is FinderWorkspaceRowView)
    XCTAssertTrue(outlineRow is FinderWorkspaceRowView)
}
```

Keep a separate assertion that a level-three heading has a larger leading constraint than a level-one heading, proving indentation remains.

- [ ] **Step 2: Run the parity test and verify RED**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-outline-style/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-outline-style/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-outline-style/scratch \
  --filter OutlineWorkspaceStyleTests
```

Expected: FAIL because Outline uses `.large`, 28-point rows, 3-point vertical spacing, and semibold H1/H2 text.

- [ ] **Step 3: Add and apply the shared presentation definition**

In `SidebarView.swift`, add:

```swift
enum SidebarTreePresentation {
    static let rowFont = NSFont.systemFont(ofSize: 13, weight: .regular)

    static func apply(to outline: NSOutlineView) {
        outline.rowSizeStyle = .medium
        outline.rowHeight = 26
        outline.selectionHighlightStyle = .sourceList
        outline.backgroundColor = .clear
    }
}
```

Call `SidebarTreePresentation.apply(to:)` from both configure methods. Do not set a custom `intercellSpacing` in Outline, so both retain the same system default. Set both Workspace and Outline cell fonts to `SidebarTreePresentation.rowFont`. Remove the heading-level weight branch while retaining the existing leading-indent calculation.

- [ ] **Step 4: Run the parity test and verify GREEN**

Run the Step 2 command again.

Expected: all `OutlineWorkspaceStyleTests` pass.

- [ ] **Step 5: Commit shared tree presentation**

```bash
git add macos/Sources/MarkLeaf/Views/SidebarView.swift \
  macos/Tests/MarkLeafTests/OutlineWorkspaceStyleTests.swift
git commit -m "fix(macos): align outline with workspace rows"
```

---

### Task 3: First-Click Outline Selection and Event Separation

**Files:**
- Create: `macos/Tests/MarkLeafTests/OutlineSelectionInteractionTests.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift:40-48, 174-219`
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift:128-220, 560-640`

**Interfaces:**
- Produces: `EditorSession.onOutlineSelectionChanged: (() -> Void)?`.
- Produces: `OutlineTreeView.configure(session:onHeadingActivated:)`.
- Produces: `OutlineTreeView.synchronizeSelection(to:)` and `reloadData(activePosition:)`.

- [ ] **Step 1: Write real AppKit click and synchronization tests**

Create a probe data source with two literal headings:

```swift
private final class OutlineProbeDataSource: NSObject, NSOutlineViewDataSource {
    let headings = [
        OutlineHeading(level: 1, text: "First", position: 0),
        OutlineHeading(level: 2, text: "Second", position: 20),
    ]

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        item == nil ? headings.count : 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        headings[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool { false }
}
```

Use the same live-window `NSEvent.mouseEvent` pattern as `WorkspaceTreeMouseInteractionTests`: construct a window containing the outline, calculate the center of row zero, post a matching left-mouse-up event to `NSApp`, then dispatch the left-mouse-down through `NSApp.sendEvent`. Add the following tests:

```swift
@MainActor
func testFirstRealClickSelectsHeadingAndActivatesExactlyOnce() throws {
    let probe = OutlineProbeDataSource()
    var activated: [Int] = []
    let outline = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
    outline.configure(session: EditorSession()) { activated.append($0.position) }
    outline.dataSource = probe
    let window = makeWindow(containing: outline)
    outline.reloadData()

    try sendSingleClick(toRow: 0, in: outline, window: window)

    XCTAssertEqual(outline.selectedRow, 0)
    XCTAssertEqual(activated, [0])
}

@MainActor
func testProgrammaticSelectionDoesNotEchoActivation() {
    let probe = OutlineProbeDataSource()
    var activated: [Int] = []
    let outline = configuredOutline(probe: probe) { activated.append($0.position) }

    outline.synchronizeSelection(to: 20)

    XCTAssertEqual(outline.selectedRow, 1)
    XCTAssertTrue(activated.isEmpty)
}

@MainActor
func testReloadRestoresActiveHeadingAndMissingPositionClearsSelection() {
    let probe = OutlineProbeDataSource()
    let outline = configuredOutline(probe: probe) { _ in }

    outline.reloadData(activePosition: 20)
    XCTAssertEqual(outline.selectedRow, 1)

    outline.reloadData(activePosition: 999)
    XCTAssertEqual(outline.selectedRow, -1)
}
```

Also add `EditorSession` message tests proving `outlineChanged` invokes only the content callback and `outlineSelectionChanged` (including `NSNull`) invokes only the selection callback.

```swift
@MainActor
func testEditorSessionSeparatesOutlineContentAndSelectionCallbacks() {
    let session = EditorSession()
    var contentChanges = 0
    var selectionChanges = 0
    session.onOutlineChanged = { contentChanges += 1 }
    session.onOutlineSelectionChanged = { selectionChanges += 1 }

    session.handleEditorMessage([
        "type": "outlineChanged",
        "payload": ["headings": [["level": 1, "text": "First", "position": 0]]],
    ])
    XCTAssertEqual(contentChanges, 1)
    XCTAssertEqual(selectionChanges, 0)

    session.handleEditorMessage([
        "type": "outlineSelectionChanged",
        "payload": ["position": 0],
    ])
    XCTAssertEqual(contentChanges, 1)
    XCTAssertEqual(selectionChanges, 1)
    XCTAssertEqual(session.activeOutlinePosition, 0)

    session.handleEditorMessage([
        "type": "outlineSelectionChanged",
        "payload": ["position": NSNull()],
    ])
    XCTAssertEqual(selectionChanges, 2)
    XCTAssertNil(session.activeOutlinePosition)
}
```

- [ ] **Step 2: Run selection tests and verify RED**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-outline-click/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-outline-click/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-outline-click/scratch \
  --filter OutlineSelectionInteractionTests
```

Expected: compilation fails because the activation closure, selection callback, and synchronization methods do not exist.

- [ ] **Step 3: Split EditorSession callbacks**

Add beside `onOutlineChanged`:

```swift
var onOutlineSelectionChanged: (() -> Void)?
```

Keep `onOutlineChanged?()` only in the `outlineChanged` message branch. Replace the selection branch with:

```swift
case "outlineSelectionChanged":
    if let position = payload?["position"] as? Int {
        activeOutlinePosition = position
    } else {
        activeOutlinePosition = nil
    }
    onOutlineSelectionChanged?()
```

- [ ] **Step 4: Route Sidebar content and selection independently**

In `SidebarView.init`, set:

```swift
session.onOutlineChanged = { [weak self] in
    DispatchQueue.main.async { self?.outlineChanged() }
}
session.onOutlineSelectionChanged = { [weak self] in
    DispatchQueue.main.async { self?.outlineSelectionChanged() }
}
```

Implement:

```swift
private func outlineChanged() {
    outlineTree.reloadData(activePosition: session.activeOutlinePosition)
}

private func outlineSelectionChanged() {
    outlineTree.synchronizeSelection(to: session.activeOutlinePosition)
}
```

Configure Outline activation with the existing session command:

```swift
outlineTree.configure(session: session) { [weak session] heading in
    session?.scrollToPosition(heading.position)
}
```

- [ ] **Step 5: Move activation after native selection and guard synchronization**

In `OutlineTreeView`, add:

```swift
private var onHeadingActivated: ((OutlineHeading) -> Void)?
private var isSynchronizingSelection = false

func configure(
    session: EditorSession,
    onHeadingActivated: ((OutlineHeading) -> Void)? = nil
) {
    self.session = session
    self.onHeadingActivated = onHeadingActivated ?? { [weak session] heading in
        session?.scrollToPosition(heading.position)
    }
    SidebarTreePresentation.apply(to: self)
}

func synchronizeSelection(to position: Int?) {
    isSynchronizingSelection = true
    defer { isSynchronizingSelection = false }
    guard let position,
          let row = (0..<numberOfRows).first(where: {
              (item(atRow: $0) as? OutlineHeading)?.position == position
          }) else {
        deselectAll(nil)
        return
    }
    selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
}

func reloadData(activePosition: Int?) {
    super.reloadData()
    synchronizeSelection(to: activePosition)
}

func outlineViewSelectionDidChange(_ notification: Notification) {
    guard !isSynchronizingSelection,
          selectedRow >= 0,
          let heading = item(atRow: selectedRow) as? OutlineHeading else { return }
    onHeadingActivated?(heading)
}
```

Change `shouldSelectItem` to validation only:

```swift
func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
    item is OutlineHeading
}
```

- [ ] **Step 6: Run selection tests and verify GREEN**

Run the Step 2 command again.

Expected: all first-click, programmatic-selection, reload-restoration, and callback-separation tests pass.

- [ ] **Step 7: Run all sidebar/outline interaction tests**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-outline-click/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-outline-click/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-outline-click/scratch \
  --filter 'Outline|Sidebar|WorkspaceTreeMouseInteractionTests'
```

Expected: PASS with no failures; folder-name double-click and Markdown single-click behaviors remain unchanged.

- [ ] **Step 8: Commit outline event separation**

```bash
git add macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Sources/MarkLeaf/Views/SidebarView.swift \
  macos/Tests/MarkLeafTests/OutlineSelectionInteractionTests.swift
git commit -m "fix(macos): preserve outline selection on first click"
```

---

### Task 4: Preserve Preferences Context During Language Refresh

**Files:**
- Create: `macos/Tests/MarkLeafTests/PreferencesLanguageRefreshTests.swift`
- Modify: `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift:19-220`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift:65-105`

**Interfaces:**
- Produces: `PreferencesWindowController.init(styles:themes:initialSelectedPageIndex:)`.
- Produces: `PreferencesWindowController.selectedPageIndex: Int`.
- Produces: `PreferencesRefreshState` and `PreferencesRestoration` value types.

- [ ] **Step 1: Write failing Preferences page and restoration-policy tests**

Create:

```swift
import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class PreferencesLanguageRefreshTests: XCTestCase {
    func testPreferencesRestoresRequestedPageAndClampsInvalidIndex() {
        let general = PreferencesWindowController(styles: [], themes: [], initialSelectedPageIndex: 3)
        let invalid = PreferencesWindowController(styles: [], themes: [], initialSelectedPageIndex: 99)

        XCTAssertEqual(general.selectedPageIndex, 3)
        XCTAssertEqual(invalid.selectedPageIndex, 4)
    }

    func testVisiblePreferencesProducesRestorationWithPageAndFrame() {
        let frame = NSRect(x: 120, y: 140, width: 640, height: 540)
        let state = PreferencesRefreshState(selectedPageIndex: 3, frame: frame, wasVisible: true)

        XCTAssertEqual(state.restoration,
                       PreferencesRestoration(selectedPageIndex: 3, frame: frame))
    }

    func testHiddenPreferencesDoesNotProduceRestoration() {
        let state = PreferencesRefreshState(
            selectedPageIndex: 3,
            frame: NSRect(x: 120, y: 140, width: 640, height: 540),
            wasVisible: false
        )

        XCTAssertNil(state.restoration)
    }
}
```

- [ ] **Step 2: Run Preferences refresh tests and verify RED**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-prefs-language/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-prefs-language/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-prefs-language/scratch \
  --filter PreferencesLanguageRefreshTests
```

Expected: compilation fails because the initial page initializer, selected-page property, and refresh-state value types do not exist.

- [ ] **Step 3: Retain and restore the Preferences tab controller**

Promote the local tab controller to a property:

```swift
private let tabViewController = NSTabViewController()

var selectedPageIndex: Int {
    get { tabViewController.selectedTabViewItemIndex }
    set {
        let upper = max(0, tabViewController.tabViewItems.count - 1)
        tabViewController.selectedTabViewItemIndex = min(max(0, newValue), upper)
    }
}
```

Change the initializer signature:

```swift
init(
    styles: [StyleDefinition],
    themes: [ColorThemeInfo],
    initialSelectedPageIndex: Int = 0
)
```

After all five tab items are added, set `selectedPageIndex = initialSelectedPageIndex`.

- [ ] **Step 4: Add explicit language-refresh state policy**

In `AppWindowManager.swift`, add:

```swift
struct PreferencesRestoration: Equatable {
    let selectedPageIndex: Int
    let frame: NSRect
}

struct PreferencesRefreshState {
    let selectedPageIndex: Int
    let frame: NSRect
    let wasVisible: Bool

    var restoration: PreferencesRestoration? {
        wasVisible ? PreferencesRestoration(selectedPageIndex: selectedPageIndex, frame: frame) : nil
    }
}
```

Refactor controller creation into a private helper accepting optional restoration:

```swift
private func makePreferences(restoration: PreferencesRestoration? = nil) -> PreferencesWindowController? {
    guard let session = primarySession else { return nil }
    let controller = PreferencesWindowController(
        styles: session.styles,
        themes: session.colorThemes,
        initialSelectedPageIndex: restoration?.selectedPageIndex ?? 0
    )
    if let frame = restoration?.frame {
        controller.window?.setFrame(frame, display: false)
    }
    controller.onSettingsChanged = { [weak self] in self?.applyPreferencesToAll() }
    return controller
}
```

Use that helper from the public show path:

```swift
func showPreferences() {
    if preferencesController == nil {
        preferencesController = makePreferences()
    }
    preferencesController?.showWindow(nil)
    preferencesController?.window?.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
}
```

Replace `applyLanguage()` with the following state-preserving sequence while retaining the existing editor and find-panel refresh calls:

```swift
func applyLanguage() {
    NativeMenuBuilder.refreshIfNeeded()

    let refreshState = preferencesController.flatMap { controller -> PreferencesRefreshState? in
        guard let window = controller.window else { return nil }
        return PreferencesRefreshState(
            selectedPageIndex: controller.selectedPageIndex,
            frame: window.frame,
            wasVisible: window.isVisible
        )
    }

    preferencesController?.window?.close()
    preferencesController = nil

    for controller in windowControllers {
        controller.applyLanguage()
    }
    findPanelController?.applyLanguage()

    guard let restoration = refreshState?.restoration,
          let controller = makePreferences(restoration: restoration) else { return }
    preferencesController = controller
    controller.showWindow(nil)
    controller.window?.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
}
```

- [ ] **Step 5: Run Preferences tests and verify GREEN**

Run the Step 2 command again.

Expected: all 3 `PreferencesLanguageRefreshTests` pass.

- [ ] **Step 6: Run existing Preferences and settings tests**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-prefs-language/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-prefs-language/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-prefs-language/scratch \
  --filter 'Preferences|Settings|L10n'
```

Expected: PASS with no failures.

- [ ] **Step 7: Commit Preferences context preservation**

```bash
git add macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift \
  macos/Sources/MarkLeaf/App/AppWindowManager.swift \
  macos/Tests/MarkLeafTests/PreferencesLanguageRefreshTests.swift
git commit -m "fix(macos): preserve preferences page on language change"
```

---

### Task 5: Full Verification, Installation, and Real UI Acceptance

**Files:**
- No new production files.
- Verify the commits and installed application produced by Tasks 1-4.

**Interfaces:**
- Consumes all behaviors produced by Tasks 1-4.
- Produces an installed `/Applications/MarkLeaf.app` whose binary matches the verified build.

- [ ] **Step 1: Run the complete macOS Swift test suite**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-native-consistency/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-native-consistency/swiftpm \
swift test --package-path macos \
  --scratch-path /tmp/markleaf-native-consistency/scratch
```

Expected: all Swift tests pass with 0 failures.

- [ ] **Step 2: Run shared EditorWeb tests**

```bash
pnpm test -- --run
```

Run from `src/EditorWeb`.

Expected: all frontend tests pass; no Windows-shared source was modified.

- [ ] **Step 3: Build and verify the application bundle**

```bash
./script/build_and_run.sh --verify
```

Run from `macos`.

Expected: frontend build, Swift build, ad-hoc signing, bundle packaging, and process verification all succeed.

- [ ] **Step 4: Install the verified bundle**

Close running MarkLeaf instances, then copy with metadata preservation:

```bash
ditto macos/dist/MarkLeaf.app /Applications/MarkLeaf.app
```

Verify the installed version remains `1.1.5` and compare SHA-256 hashes of both `Contents/MacOS/MarkLeaf` binaries.

- [ ] **Step 5: Perform real Outline acceptance**

Open a controlled Markdown file containing at least three heading levels. In the installed application:

1. Compare Workspace and Outline: both must use regular 13-point rows with matching height and gray selection shape.
2. Click two different Outline rows once each: the clicked row must turn gray immediately on every first click.
3. Confirm the editor scrolls to the matching heading once.
4. Scroll the editor to another heading: Outline selection must follow without flashing.

- [ ] **Step 6: Perform real language and Preferences acceptance**

Open Preferences on General, move the window away from center, and change language. Confirm the rebuilt Preferences window remains on General and at the same frame. The hidden-window restoration policy is covered by `testHiddenPreferencesDoesNotProduceRestoration`; there is currently no separate user-facing language control outside Preferences.

- [ ] **Step 7: Perform real recovery-window acceptance**

Create or retain one recovery snapshot, relaunch, and inspect the recovery dialog in Simplified Chinese and English:

- Simplified Chinese contains no parenthetical abnormal-exit phrase.
- English title and introductory sentence are both English.
- Save As, Discard All, and Cancel remain functional and localized.

- [ ] **Step 8: Final repository verification**

```bash
git diff --check
git status --short
git log -6 --oneline
```

Expected: no unstaged/uncommitted implementation changes, no whitespace errors, and the four implementation commits plus design/plan commits are visible.
